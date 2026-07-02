using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Users;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Users;

/// <summary>
/// Admin/staff user reads and admin user management (features 15 + 15A). The list is <c>AsNoTracking</c>, projected
/// straight to <see cref="UserSummaryDto"/> (never the Identity entity) and always paged (rubric §8.2). The name/email
/// search runs at the database (case-insensitive, no load-all-then-filter); the primary role is JOINed in the same
/// projection so there is no per-row role query (no N+1). Detail + the mutations (activate/deactivate, role assignment)
/// go through <see cref="UserManager{AppUser}"/> so Identity stays the authority on roles; every mutation is admin-only
/// (enforced at the controller) and guards against the acting admin changing their own account.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly CourtlyDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly ICurrentUser _currentUser;

    public UserService(CourtlyDbContext db, UserManager<AppUser> userManager, ICurrentUser currentUser)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<UserSummaryDto>> SearchAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // ILike filters case-insensitively at the database (Postgres); the InMemory test provider does not
            // translate ILike, so fall back to a lowered Contains there — same case-insensitive semantics as the
            // reference services.
            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                query = query.Where(u =>
                    EF.Functions.ILike(u.FirstName, pattern)
                    || EF.Functions.ILike(u.LastName, pattern)
                    || (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
            }
            else
            {
                var lowered = term.ToLower();
                query = query.Where(u =>
                    u.FirstName.ToLower().Contains(lowered)
                    || u.LastName.ToLower().Contains(lowered)
                    || (u.Email != null && u.Email.ToLower().Contains(lowered)));
            }
        }

        return await query
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Select(u => new UserSummaryDto(
                u.Id,
                u.FirstName + " " + u.LastName,
                u.Email,
                // Primary role name (JOINed in-projection; null when the user has no role). Feature 15A surfaces the
                // full role set — feature 15's picker only needs an informational label.
                (from ur in _db.UserRoles
                 join role in _db.Roles on ur.RoleId equals role.Id
                 where ur.UserId == u.Id
                 orderby role.Name
                 select role.Name).FirstOrDefault(),
                u.IsActive))
            .ToPagedResultAsync(pagination, ct);
    }

    public async Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException($"User {id} was not found.");

        var roles = await _userManager.GetRolesAsync(user);

        // Resolve the city name in one read-only lookup (never the whole city entity on the wire); null when the user
        // has no city set.
        string? cityName = user.CityId is null
            ? null
            : await _db.Cities.AsNoTracking()
                .Where(c => c.Id == user.CityId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);

        return new UserDetailDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.FirstName + " " + user.LastName,
            user.Email,
            user.PhoneNumber,
            cityName,
            roles.ToList(),
            user.IsActive,
            user.CreatedAtUtc,
            id == SeedIds.UserAdmin);
    }

    public async Task<UserDetailDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        // Ownership always comes from the token — an admin can never lock themselves out.
        if (_currentUser.UserId == id)
        {
            throw new BusinessException("You cannot deactivate your own account.");
        }

        // The seeded super administrator is protected: no admin can deactivate it (guards against locking the whole
        // system out of admin access). Reactivating it is still allowed.
        if (id == SeedIds.UserAdmin && !isActive)
        {
            throw new BusinessException("The super administrator account cannot be deactivated.");
        }

        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException($"User {id} was not found.");

        user.IsActive = isActive;
        await _userManager.UpdateAsync(user);

        return await GetByIdAsync(id, ct);
    }

    public async Task<UserDetailDto> AssignRoleAsync(Guid id, string role, CancellationToken ct = default)
    {
        // An admin cannot change their own role (e.g. demote themselves out of Admin). The role value itself is
        // validated by AssignRoleRequestValidator before this runs.
        if (_currentUser.UserId == id)
        {
            throw new BusinessException("You cannot change your own role.");
        }

        // The seeded super administrator must always stay Admin — no one can demote it.
        if (id == SeedIds.UserAdmin)
        {
            throw new BusinessException("The super administrator's role cannot be changed.");
        }

        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException($"User {id} was not found.");

        // Single-role model: drop whatever the user has, then grant the requested role. Identity stays the authority.
        // Remove + add are two saves, so wrap them in one transaction (relational only — the InMemory test provider has
        // no transaction support) to avoid ever leaving the user role-less if the second call fails.
        var current = await _userManager.GetRolesAsync(user);

        await using var tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        var removed = await _userManager.RemoveFromRolesAsync(user, current);
        if (!removed.Succeeded)
        {
            throw new BusinessException(DescribeIdentityErrors(removed));
        }

        var added = await _userManager.AddToRoleAsync(user, role);
        if (!added.Succeeded)
        {
            throw new BusinessException(DescribeIdentityErrors(added));
        }

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return await GetByIdAsync(id, ct);
    }

    private static string DescribeIdentityErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
