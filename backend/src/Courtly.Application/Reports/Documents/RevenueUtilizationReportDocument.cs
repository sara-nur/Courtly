using System.Globalization;
using Courtly.Contracts.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Courtly.Application.Reports.Documents;

/// <summary>
/// The revenue &amp; court-utilisation report (feature 20) for a month (± one court), rendered as a
/// downloadable/printable PDF: a totals summary over a per-court table (bookings, net revenue, utilisation %). The
/// figures reuse the F19 analytics predicates, so they reconcile with the dashboard for the same month.
/// </summary>
internal sealed class RevenueUtilizationReportDocument : IDocument
{
    private readonly RevenueUtilizationReportData _data;

    public RevenueUtilizationReportDocument(RevenueUtilizationReportData data) => _data = data;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        var subtitle = $"Month: {_data.MonthLabel}   ·   Court: {_data.CourtLabel}";
        ReportLayout.Page(container, "Revenue & court utilisation", subtitle, _data.GeneratedAtUtc, Body);
    }

    private void Body(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(10).Element(Summary);

            if (_data.Rows.Count == 0)
            {
                col.Item().PaddingTop(24).AlignCenter()
                    .Text("No activity for this month.").Italic().FontColor(ReportLayout.Muted);
                return;
            }

            col.Item().Element(Table);
        });
    }

    private void Summary(IContainer container)
    {
        container.Text(t =>
        {
            t.Span("Total bookings: ").SemiBold();
            t.Span(_data.TotalBookings.ToString(CultureInfo.InvariantCulture));
            t.Span("        Total revenue: ").SemiBold();
            t.Span(Money(_data.TotalRevenue));
            t.Span("        Overall utilisation: ").SemiBold();
            t.Span(Percent(_data.OverallUtilizationPct));
        });
    }

    private void Table(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3f);    // court
                columns.ConstantColumn(80);    // bookings
                columns.ConstantColumn(90);    // revenue
                columns.ConstantColumn(90);    // utilisation
            });

            table.Header(header =>
            {
                header.Cell().HeadText("Court");
                header.Cell().AlignRight().HeadText("Bookings");
                header.Cell().AlignRight().HeadText("Revenue");
                header.Cell().AlignRight().HeadText("Utilisation");
            });

            for (var i = 0; i < _data.Rows.Count; i++)
            {
                var r = _data.Rows[i];
                var cell = ReportLayout.BodyCell(i % 2 == 1);

                table.Cell().Element(cell).Text(r.CourtName).FontSize(8);
                table.Cell().Element(cell).AlignRight().Text(r.Bookings.ToString(CultureInfo.InvariantCulture)).FontSize(8);
                table.Cell().Element(cell).AlignRight().Text(Money(r.Revenue)).FontSize(8);
                table.Cell().Element(cell).AlignRight().Text(Percent(r.UtilizationPct)).FontSize(8);
            }

            // Totals row (bold, no zebra).
            var totalCell = ReportLayout.BodyCell(false);
            table.Cell().Element(totalCell).Text("Total").SemiBold().FontSize(8);
            table.Cell().Element(totalCell).AlignRight().Text(_data.TotalBookings.ToString(CultureInfo.InvariantCulture)).SemiBold().FontSize(8);
            table.Cell().Element(totalCell).AlignRight().Text(Money(_data.TotalRevenue)).SemiBold().FontSize(8);
            table.Cell().Element(totalCell).AlignRight().Text(Percent(_data.OverallUtilizationPct)).SemiBold().FontSize(8);
        });
    }

    private static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Percent(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
