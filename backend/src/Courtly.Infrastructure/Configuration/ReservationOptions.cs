namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// Reservation engine tunables (feature 14), bound from <c>.env</c> (rubric §3.3 — configuration lives in <c>.env</c>,
/// never hardcoded). <see cref="HoldMinutes"/> is how long an unpaid <c>Pending</c> reservation may hold its slot
/// before it becomes eligible for auto-cancellation: F14 stamps each new reservation's <c>HoldExpiresAtUtc</c> from
/// this value; the Worker scan that actually releases expired holds lands in feature 17.
/// </summary>
public sealed class ReservationOptions
{
    /// <summary>Minutes an unpaid Pending hold survives before it can be auto-cancelled. Default 15.</summary>
    public int HoldMinutes { get; set; } = 15;

    /// <summary>How often the Worker scans for expired Pending holds, in seconds. Bound from <c>RESERVATION_HOLD_SCAN_SECONDS</c>; default 60.</summary>
    public int HoldScanSeconds { get; set; } = 60;

    /// <summary>How often the Worker scans for Confirmed reservations whose slot has ended, to auto-complete them, in
    /// seconds. Bound from <c>RESERVATION_AUTOCOMPLETE_SCAN_SECONDS</c>; default 300.</summary>
    public int AutoCompleteScanSeconds { get; set; } = 300;
}
