using Courtly.Contracts.Reports;

namespace Courtly.Application.Reports;

/// <summary>
/// PDF report engine (feature 20). Builds the report data with the shared <see cref="Analytics.AnalyticsFilters"/>
/// predicates (so the figures reconcile with the F19 dashboard) and renders each one to a downloadable/printable PDF
/// via QuestPDF. The <c>Build*</c> methods are split out from the <c>Generate*</c> renderers so the aggregation can be
/// asserted on exact numbers in tests without parsing a PDF.
/// </summary>
public interface IReportService
{
    /// <summary>The reservations report data (operational booking listing) for the given date/court/status filter.</summary>
    Task<ReservationsReportData> BuildReservationsDataAsync(
        ReservationsReportQuery filter, CancellationToken ct = default);

    /// <summary>The reservations report rendered to PDF bytes (<c>application/pdf</c>).</summary>
    Task<byte[]> GenerateReservationsReportAsync(
        ReservationsReportQuery filter, CancellationToken ct = default);

    /// <summary>The revenue &amp; court-utilisation report data for the given month (± one court).</summary>
    Task<RevenueUtilizationReportData> BuildRevenueUtilizationDataAsync(
        RevenueUtilizationReportQuery filter, CancellationToken ct = default);

    /// <summary>The revenue &amp; court-utilisation report rendered to PDF bytes (<c>application/pdf</c>).</summary>
    Task<byte[]> GenerateRevenueUtilizationReportAsync(
        RevenueUtilizationReportQuery filter, CancellationToken ct = default);
}
