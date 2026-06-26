using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Users;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Users;

/// <summary>
/// Admin/staff user reads (feature 15 slice). The list is <c>AsNoTracking</c>, projected straight to
/// <see cref="UserSummaryDto"/> (never the Identity entity) and always paged (rubric §8.2). The name/email search runs
/// at the database (case-insensitive, no load-all-then-filter); the primary role is JOINed in the same projection so
/// there is no per-row role query (no N+1). Feature 15A extends this service with the rest of the Users surface.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly CourtlyDbContext _db;

    public UserService(CourtlyDbContext db)
    {
        _db = db;
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
}
