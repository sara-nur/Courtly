using Courtly.Application.Users;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Users;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Admin/staff user lookup and admin user management (features 15 + 15A). The controller is thin: it model-binds, calls
/// <see cref="IUserService"/>, and returns the DTO — no business logic or DbContext here. Every endpoint exposes other
/// users' data, so the whole controller is <b>Admin/Staff</b> only (rubric §5: role-based authz on admin endpoints);
/// the two mutating endpoints (activate/deactivate, role assignment) are further restricted to <b>Admin</b>. The
/// service throws the app's custom exceptions and the exception middleware maps them to a standardized
/// <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = AdminOrStaff)]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class UsersController : ControllerBase
{
    private const string AdminOrStaff = Roles.Admin + "," + Roles.Staff;

    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    /// <summary>One page of users, ordered by name, optionally filtered by a case-insensitive name/email search.
    /// Backs the feature 15 "+ New Booking" customer picker.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _users.SearchAsync(pagination, search, ct));

    /// <summary>One user's full admin detail (all roles + resolved city). The user's reservations are fetched
    /// separately by the client via <c>GET /api/reservations?userId=</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _users.GetByIdAsync(id, ct));

    /// <summary>Activates/deactivates a user (Admin only). The acting admin cannot deactivate their own account.
    /// </summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:guid}/active")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailDto>> SetActive(
        Guid id, SetUserActiveRequest request, CancellationToken ct)
        => Ok(await _users.SetActiveAsync(id, request.IsActive, ct));

    /// <summary>Replaces a user's single role (Admin only). The acting admin cannot change their own role.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailDto>> AssignRole(
        Guid id, AssignRoleRequest request, CancellationToken ct)
        => Ok(await _users.AssignRoleAsync(id, request.Role, ct));
}
