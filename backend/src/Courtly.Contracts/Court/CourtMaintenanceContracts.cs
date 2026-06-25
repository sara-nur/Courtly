using Courtly.Domain.Enums;

namespace Courtly.Contracts.Court;

/// <summary>
/// Court status &amp; maintenance contracts (feature 12). A <see cref="CourtMaintenanceLogDto"/> is one maintenance
/// WINDOW for a court — its <see cref="Status"/> moves through the centralized maintenance state machine
/// (Scheduled → InProgress → Completed/Cancelled). <see cref="StatusName"/> is the human label so the UI never has
/// to map the raw enum; <see cref="PerformedByName"/> is the resolved actor name (JOINed, never the raw user id) for
/// the "who/when/why" status history. DTOs only on the wire — entities never leave the service.
/// </summary>
public sealed record CourtMaintenanceLogDto(
    long Id,
    long CourtId,
    MaintenanceStatus Status,
    string StatusName,
    string Reason,
    DateTime StartUtc,
    DateTime? EndUtc,
    string? PerformedByName,
    DateTime CreatedAtUtc);

/// <summary>
/// Opens a maintenance window on a court. <see cref="StartUtc"/> null or in the past ⇒ the court is put under
/// maintenance immediately (an <c>InProgress</c> window starting now); a future <see cref="StartUtc"/> ⇒ a
/// <c>Scheduled</c> window. <see cref="EndUtc"/> is the optional planned end (open-ended when null). The server owns
/// the timing decision and the actor (from the JWT) — the client supplies only the reason and the planned window.
/// </summary>
public sealed record CreateMaintenanceRequest(
    string Reason,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null);
