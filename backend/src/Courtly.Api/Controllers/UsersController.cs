using Courtly.Application.Users;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Users;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Admin/staff user lookup (feature 15 slice). The controller is thin: it model-binds, calls
/// <see cref="IUserService"/>, and returns the DTO — no business logic or DbContext here. Every endpoint exposes other
/// users' data, so the whole controller is <b>Admin/Staff</b> only (rubric §5: role-based authz on admin endpoints).
/// Feature 15A extends this controller with user detail, activate/deactivate, role assignment and admin-edit profile.
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
}
