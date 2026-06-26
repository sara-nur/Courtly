using Courtly.Contracts.Common;
using Courtly.Contracts.Users;

namespace Courtly.Application.Users;

/// <summary>
/// Admin/staff read access to application users. Feature 15 needs only the paginated, searchable lookup that backs the
/// "+ New Booking" customer picker; feature 15A extends this service with detail (+ the user's reservations),
/// activate/deactivate, role assignment and admin-edit profile.
/// </summary>
public interface IUserService
{
    /// <summary>Paginated user list, optionally filtered by a name/email search term, ordered by name. Read-only,
    /// projected to <see cref="UserSummaryDto"/> (never the Identity entity).</summary>
    Task<PagedResult<UserSummaryDto>> SearchAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);
}
