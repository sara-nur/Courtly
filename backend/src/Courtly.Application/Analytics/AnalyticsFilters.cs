using Courtly.Domain.Entities;
using Courtly.Domain.Enums;

namespace Courtly.Application.Analytics;

/// <summary>
/// The shared "what counts" predicates behind every analytics figure (dashboard F19 + reports F20). Centralising them
/// here means the reservations/revenue reports reconcile with the dashboard <b>by construction</b> — both apply the
/// exact same definitions of a counted reservation, a net-revenue payment and an available slot (rubric §8.1, DRY).
/// </summary>
/// <remarks>
/// Each method takes an already-sourced query (so the caller owns <c>AsNoTracking</c>) and adds only the candidate-set
/// predicates: active court, not under maintenance (the F12 exclusion set), optional court-type narrowing. The
/// <i>time</i> predicate is always added by the caller, because the relevant timestamp differs per metric
/// (<c>CreatedAtUtc</c> for volume, <c>PaidAtUtc</c> for revenue, slot <c>StartUtc</c> for occupancy).
/// </remarks>
public static class AnalyticsFilters
{
    /// <summary>Reservations that count toward analytics: Confirmed/Completed (a Pending hold or a Cancelled booking is
    /// not a real booking), on an active court that is not under maintenance, optionally narrowed to one court type.</summary>
    public static IQueryable<Reservation> CountedReservations(
        IQueryable<Reservation> source, long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = source.Where(r =>
            (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Completed)
            && r.Court.IsActive
            && !maintenanceIds.Contains(r.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(r => r.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }

    /// <summary>Succeeded payments on candidate courts — the net-revenue source. Refunded payments are excluded by the
    /// status filter, so summing these already nets refunds out (subtracting refund rows would double-count the loss).</summary>
    public static IQueryable<Payment> CountedPayments(
        IQueryable<Payment> source, long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = source.Where(p =>
            p.Status == PaymentStatus.Succeeded
            && p.PaidAtUtc != null
            && p.Reservation.Court.IsActive
            && !maintenanceIds.Contains(p.Reservation.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(p => p.Reservation.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }

    /// <summary>Active slots on candidate courts — the occupancy denominator.</summary>
    public static IQueryable<TimeSlot> CandidateSlots(
        IQueryable<TimeSlot> source, long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = source.Where(s =>
            s.IsActive
            && s.Court.IsActive
            && !maintenanceIds.Contains(s.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(s => s.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }
}
