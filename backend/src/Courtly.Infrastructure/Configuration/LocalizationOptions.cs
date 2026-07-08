namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// Business-locale settings. <see cref="TimeZoneId"/> is the IANA time zone the courts operate in (e.g.
/// <c>Europe/Sarajevo</c>): admin-entered opening hours and per-day availability are business-<i>local</i> times, so
/// slot generation interprets an hour like 08:00 in this zone and only then converts it to UTC for storage. Bound from
/// <c>COURT_TIME_ZONE</c> (rubric §3.3 — configuration lives in <c>.env</c>, never hardcoded). IANA ids resolve on all
/// target platforms via the .NET 6+ ICU time-zone data.
/// </summary>
public sealed class LocalizationOptions
{
    /// <summary>IANA id of the courts' local time zone. Default <c>Europe/Sarajevo</c>.</summary>
    public string TimeZoneId { get; set; } = "Europe/Sarajevo";
}
