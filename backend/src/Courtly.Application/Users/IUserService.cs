using Courtly.Contracts.Common;
using Courtly.Contracts.Users;

namespace Courtly.Application.Users;

/// <summary>
/// Admin/staff access to application users. Feature 15 needs only the paginated, searchable lookup that backs the
/// "+ New Booking" customer picker; feature 15A extends this service with user detail, activate/deactivate and role
/// assignment. The user's reservations are not returned here — the client reuses <c>GET /api/reservations?userId=</c>.
/// </summary>
public interface IUserService
{
    /// <summary>Paginated user list, optionally filtered by a name/email search term, ordered by name. Read-only,
    /// projected to <see cref="UserSummaryDto"/> (never the Identity entity).</summary>
    Task<PagedResult<UserSummaryDto>> SearchAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>One user's full admin detail (all roles + resolved city). Throws
    /// <c>NotFoundException</c> when the user does not exist. Admin/staff read.</summary>
    Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Activates/deactivates a user (admin only). Rejects deactivating the acting admin's own account
    /// (<c>BusinessException</c>) and throws <c>NotFoundException</c> when the user does not exist. Returns the
    /// updated detail.</summary>
    Task<UserDetailDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Replaces a user's single role (admin only). Rejects changing the acting admin's own role
    /// (<c>BusinessException</c>) and throws <c>NotFoundException</c> when the user does not exist. Returns the
    /// updated detail.</summary>
    Task<UserDetailDto> AssignRoleAsync(Guid id, string role, CancellationToken ct = default);
}
