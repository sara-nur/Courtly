using Courtly.Application.Dashboard;
using Courtly.Contracts.Dashboard;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// The dashboard analytics API (feature 19). A thin controller over <see cref="IDashboardService"/>: it model-binds the
/// filters, calls the service, and returns the composite DTO — every aggregate, the prior-period deltas and the health
/// rule live in the service, never here. Admin/Staff only (the desktop admin overview); the service computes each
/// figure with a single database aggregate and caches the result for a short TTL.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = AdminOrStaff)]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class DashboardController : ControllerBase
{
    // Constant role list for [Authorize] (constant string concatenation → valid attribute argument).
    private const string AdminOrStaff = Roles.Admin + "," + Roles.Staff;

    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    /// <summary>The admin overview: 4 KPIs (each with its prior-period delta), the revenue / popular-courts /
    /// peak-hours series, and the health banner. Filters are optional (date range defaults to the last 30 days; an
    /// omitted court type spans every type).</summary>
    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics(
        [FromQuery] DashboardFiltersQuery filter, CancellationToken ct)
        => Ok(await _dashboard.GetMetricsAsync(filter, ct));
}
