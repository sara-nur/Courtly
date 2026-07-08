using Courtly.Application.Reports;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reports;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// The PDF reports API (feature 20). A thin controller over <see cref="IReportService"/>. Each report exposes a
/// <c>/data</c> endpoint that returns the report rows as JSON (so the admin renders the report in-app as a table) and a
/// PDF endpoint that returns the rendered, downloadable/printable <c>application/pdf</c>. Both reports are Admin
/// only; the aggregation + rendering live in the service.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Admin)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class ReportsController : ControllerBase
{
    private const string Pdf = "application/pdf";

    private readonly IReportService _reports;

    public ReportsController(IReportService reports)
    {
        _reports = reports;
    }

    // --- Reservations report ---------------------------------------------------------------------

    /// <summary>The reservations report data (rows + totals) for a date range / court / status — rendered as a table
    /// in the admin UI. All filters optional (date range defaults to the last 30 days).</summary>
    [HttpGet("reservations/data")]
    [Produces("application/json")]
    public async Task<ActionResult<ReservationsReportData>> ReservationsData(
        [FromQuery] ReservationsReportQuery filter, CancellationToken ct)
        => Ok(await _reports.BuildReservationsDataAsync(filter, ct));

    /// <summary>The same reservations report as a downloadable/printable PDF.</summary>
    [HttpGet("reservations")]
    [Produces(Pdf)]
    public async Task<IActionResult> Reservations([FromQuery] ReservationsReportQuery filter, CancellationToken ct)
    {
        var bytes = await _reports.GenerateReservationsReportAsync(filter, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, Pdf, "reservations-report.pdf");
    }

    // --- Revenue & court-utilisation report ------------------------------------------------------

    /// <summary>The revenue &amp; court-utilisation report data (per-court rows + totals) for a month (± one court) —
    /// rendered as a table in the admin UI. Defaults to the current month.</summary>
    [HttpGet("revenue-utilization/data")]
    [Produces("application/json")]
    public async Task<ActionResult<RevenueUtilizationReportData>> RevenueUtilizationData(
        [FromQuery] RevenueUtilizationReportQuery filter, CancellationToken ct)
        => Ok(await _reports.BuildRevenueUtilizationDataAsync(filter, ct));

    /// <summary>The same revenue &amp; court-utilisation report as a downloadable/printable PDF. The figures reconcile
    /// with the dashboard for the same month.</summary>
    [HttpGet("revenue-utilization")]
    [Produces(Pdf)]
    public async Task<IActionResult> RevenueUtilization(
        [FromQuery] RevenueUtilizationReportQuery filter, CancellationToken ct)
    {
        var bytes = await _reports.GenerateRevenueUtilizationReportAsync(filter, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, Pdf, "revenue-utilization.pdf");
    }
}
