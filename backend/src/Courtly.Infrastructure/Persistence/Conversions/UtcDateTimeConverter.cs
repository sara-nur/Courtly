using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Courtly.Infrastructure.Persistence.Conversions;

/// <summary>
/// Guarantees every <see cref="DateTime"/> is <see cref="DateTimeKind.Utc"/> so it always maps cleanly to
/// PostgreSQL <c>timestamptz</c>. The app stores UTC only (rubric A.4), but values arriving from JSON
/// deserialization or model binding come back <c>Unspecified</c> and would otherwise throw on write. We treat
/// non-UTC kinds as already-UTC (label, don't shift) — correct for an all-UTC system. Applied globally in
/// <c>CourtlyDbContext.ConfigureConventions</c>, so it also covers nullable <c>DateTime?</c> properties.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
