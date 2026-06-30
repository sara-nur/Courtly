using System.Globalization;
using Courtly.Contracts.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Courtly.Application.Reports.Documents;

/// <summary>
/// The reservations report (feature 20) — the operational booking listing for a date range / court / status, rendered
/// as a downloadable/printable PDF: a totals + per-status summary line over a table of bookings (reference, customer,
/// court, slot window, status, paid, amount). Reference is the human <c>#RES-001</c>, never the raw id (rubric §6).
/// </summary>
internal sealed class ReservationsReportDocument : IDocument
{
    private readonly ReservationsReportData _data;

    public ReservationsReportDocument(ReservationsReportData data) => _data = data;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        var subtitle =
            $"Period {_data.FromUtc:yyyy-MM-dd} → {_data.ToUtc:yyyy-MM-dd} UTC   ·   Court: {_data.CourtLabel}   ·   Status: {_data.StatusLabel}";
        ReportLayout.Page(container, "Reservations report", subtitle, _data.GeneratedAtUtc, Body);
    }

    private void Body(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(10).Element(Summary);

            if (_data.Rows.Count == 0)
            {
                col.Item().PaddingTop(24).AlignCenter()
                    .Text("No reservations match these filters.").Italic().FontColor(ReportLayout.Muted);
                return;
            }

            col.Item().Element(Table);
        });
    }

    private void Summary(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Total bookings: ").SemiBold();
                t.Span(_data.TotalCount.ToString(CultureInfo.InvariantCulture));
                t.Span("        Total amount: ").SemiBold();
                t.Span(Money(_data.TotalAmount));
            });

            if (_data.StatusBreakdown.Count > 0)
            {
                row.RelativeItem().AlignRight().Text(
                        string.Join("    ", _data.StatusBreakdown.Select(s => $"{s.StatusName}: {s.Count}")))
                    .FontSize(8).FontColor(ReportLayout.Muted);
            }
        });
    }

    private void Table(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(58);    // reference
                columns.RelativeColumn(2.4f);  // customer
                columns.RelativeColumn(1.4f);  // court
                columns.RelativeColumn(1.5f);  // date & time
                columns.ConstantColumn(64);    // status
                columns.ConstantColumn(34);    // paid
                columns.ConstantColumn(58);    // amount
            });

            table.Header(header =>
            {
                header.Cell().HeadText("Ref");
                header.Cell().HeadText("Customer");
                header.Cell().HeadText("Court");
                header.Cell().HeadText("Date & time");
                header.Cell().HeadText("Status");
                header.Cell().HeadText("Paid");
                header.Cell().AlignRight().HeadText("Amount");
            });

            for (var i = 0; i < _data.Rows.Count; i++)
            {
                var r = _data.Rows[i];
                var cell = ReportLayout.BodyCell(i % 2 == 1);

                table.Cell().Element(cell).Text(r.Reference).FontSize(8);

                table.Cell().Element(cell).Text(t =>
                {
                    // Line() breaks AFTER its text, so the name goes on Line() and the email follows on the next row —
                    // otherwise the two run together with no separation.
                    if (!string.IsNullOrWhiteSpace(r.Email))
                    {
                        t.Line(r.Customer).FontSize(8);
                        t.Span(r.Email!).FontSize(7).FontColor(ReportLayout.Muted);
                    }
                    else
                    {
                        t.Span(r.Customer).FontSize(8);
                    }
                });

                table.Cell().Element(cell).Text(r.CourtName).FontSize(8);

                table.Cell().Element(cell).Text(t =>
                {
                    t.Line($"{r.SlotStartUtc:yyyy-MM-dd}").FontSize(8);
                    t.Span($"{r.SlotStartUtc:HH:mm}–{r.SlotEndUtc:HH:mm} UTC").FontSize(7).FontColor(ReportLayout.Muted);
                });

                table.Cell().Element(cell).Text(r.StatusName).FontSize(8);

                table.Cell().Element(cell).Text(r.IsPaid ? "Yes" : "No")
                    .FontSize(8).FontColor(r.IsPaid ? ReportLayout.Primary : ReportLayout.Muted);

                table.Cell().Element(cell).AlignRight().Text(Money(r.Amount)).FontSize(8);
            }
        });
    }

    private static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
}
