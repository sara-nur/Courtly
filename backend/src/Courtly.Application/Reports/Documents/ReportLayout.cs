using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Courtly.Application.Reports.Documents;

/// <summary>
/// Shared QuestPDF chrome for the feature-20 reports — the page scaffold, the "Courtly Admin" header, the footer with
/// the generated-at stamp + page numbers, and the table cell styles. Kept in one place so both report documents look
/// identical and the styling stays DRY (rubric §8.1). Colours echo the admin UI's blue accent + neutral greys.
/// </summary>
internal static class ReportLayout
{
    public const string Primary = "#2563EB";
    public const string Ink = "#1F2937";
    public const string Muted = "#6B7280";
    public const string Line = "#E5E7EB";
    public const string Zebra = "#F9FAFB";

    /// <summary>The standard page: A4, margins, default text style, a titled header and the shared footer; the caller
    /// supplies the body.</summary>
    public static void Page(
        IDocumentContainer container, string title, string subtitle, DateTime generatedAtUtc, Action<IContainer> body)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink));
            page.Header().Element(h => Header(h, title, subtitle));
            page.Content().PaddingVertical(10).Element(body);
            page.Footer().Element(f => Footer(f, generatedAtUtc));
        });
    }

    private static void Header(IContainer container, string title, string subtitle)
    {
        container.Column(col =>
        {
            col.Item().Text("Courtly Admin").FontSize(9).SemiBold().FontColor(Primary);
            col.Item().Text(title).FontSize(18).Bold().FontColor(Ink);
            col.Item().PaddingTop(2).Text(subtitle).FontSize(9).FontColor(Muted);
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Line);
        });
    }

    private static void Footer(IContainer container, DateTime generatedAtUtc)
    {
        container.PaddingTop(6).Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Line);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC")
                    .FontSize(8).FontColor(Muted);
                row.ConstantItem(120).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    /// <summary>A coloured table heading cell.</summary>
    public static IContainer HeaderCell(IContainer container) =>
        container.Background(Primary).PaddingVertical(5).PaddingHorizontal(5);

    /// <summary>A body cell with optional zebra striping and a hairline bottom border.</summary>
    public static Func<IContainer, IContainer> BodyCell(bool zebra) => container =>
        container.Background(zebra ? Zebra : Colors.White)
            .PaddingVertical(4).PaddingHorizontal(5)
            .BorderBottom(0.5f).BorderColor(Line);

    /// <summary>Heading-cell text style (white on the primary colour).</summary>
    public static TextSpanDescriptor HeadText(this IContainer cell, string text) =>
        cell.Element(HeaderCell).Text(text).FontColor(Colors.White).SemiBold().FontSize(8);
}
