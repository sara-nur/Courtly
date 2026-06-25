using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Courts.Maintenance;

/// <summary>
/// Court status &amp; maintenance (feature 12). Each <c>CourtMaintenanceLog</c> row is one maintenance WINDOW; its
/// <see cref="MaintenanceStatus"/> moves only through <see cref="MaintenanceStateMachine"/> (transitions are never
/// decided in the controller — rubric §7). The actor is taken from the JWT via <see cref="ICurrentUser"/> (never the
/// route/body) and every timestamp from <see cref="IClock"/> (UTC). Reads are <c>AsNoTracking</c>, projected to
/// <see cref="CourtMaintenanceLogDto"/> (never entities) and paged; the human <c>StatusName</c> and the resolved
/// <c>PerformedByName</c> are built in memory after materialization so the projection stays provider-agnostic
/// (Npgsql in prod, EF InMemory in tests).
///
/// A court is "under maintenance" at an instant when it has an OPEN window (Scheduled/InProgress) covering that
/// instant; the same open-window rule, applied over a range, powers the exclusion query the booking (F13) and
/// dashboard (F19) features reuse. Overlapping open windows are rejected on create so "is the court under
/// maintenance" stays unambiguous.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(
        CourtlyDbContext db, IClock clock, ICurrentUser currentUser, ILogger<MaintenanceService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PagedResult<CourtMaintenanceLogDto>> GetHistoryAsync(
        long courtId, PaginationQuery pagination, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        // Newest-first; project to the provider-agnostic row, page at the DB, then map StatusName/PerformedByName.
        var paged = await Project(
                _db.CourtMaintenanceLogs.AsNoTracking()
                    .Where(m => m.CourtId == courtId)
                    .OrderByDescending(m => m.Id))
            .ToPagedResultAsync(pagination, ct);

        return new PagedResult<CourtMaintenanceLogDto>(
            paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<CourtMaintenanceLogDto> CreateAsync(
        long courtId, CreateMaintenanceRequest request, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        var reason = (request.Reason ?? string.Empty).Trim();
        var now = _clock.UtcNow;

        // The server owns the timing: a missing/past start means "put under maintenance now" (InProgress); a future
        // start is a planned (Scheduled) window.
        var startUtc = request.StartUtc ?? now;
        MaintenanceStatus status;
        if (startUtc <= now)
        {
            startUtc = now;
            status = MaintenanceStatus.InProgress;
        }
        else
        {
            status = MaintenanceStatus.Scheduled;
        }

        var endUtc = request.EndUtc;
        if (endUtc.HasValue && endUtc.Value <= startUtc)
        {
            // When the start was omitted/in the past it has been pulled to "now", so an end before that is really
            // an end in the past — say so, rather than the misleading "after its start".
            throw new ValidationException(status == MaintenanceStatus.Scheduled
                ? "The maintenance end must be after the scheduled start."
                : "The maintenance end must be in the future.");
        }

        if (await OverlapsOpenWindowAsync(courtId, startUtc, endUtc, excludeLogId: null, ct))
        {
            throw new BusinessException(
                "This court already has a maintenance window that overlaps the requested period.");
        }

        var entity = new Domain.Entities.CourtMaintenanceLog
        {
            CourtId = courtId,
            Status = status,
            Reason = reason,
            StartUtc = startUtc,
            EndUtc = endUtc,
            PerformedByUserId = _currentUser.UserId,
            CreatedAtUtc = now,
        };
        _db.CourtMaintenanceLogs.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Opened maintenance window {LogId} on court {CourtId} (status={Status}).", entity.Id, courtId, status);

        return await ProjectAsync(entity.Id, ct);
    }

    public async Task<CourtMaintenanceLogDto> StartAsync(long courtId, long logId, CancellationToken ct = default)
    {
        var window = await GetWindowAsync(courtId, logId, ct);
        MaintenanceStateMachine.EnsureCanTransition(window.Status, MaintenanceStatus.InProgress);

        var now = _clock.UtcNow;
        window.Status = MaintenanceStatus.InProgress;
        // Starting EARLY (the planned start is still in the future) means the court is unavailable from NOW — leaving
        // a future StartUtc would make the "covering now" predicate (StartUtc <= now) wrongly skip it. A window
        // started on/after its planned time KEEPS that planned StartUtc, preserving the scheduled time in the history.
        if (window.StartUtc > now)
        {
            window.StartUtc = now;
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Started maintenance window {LogId} on court {CourtId}.", logId, courtId);
        return await ProjectAsync(logId, ct);
    }

    public async Task<CourtMaintenanceLogDto> CompleteAsync(long courtId, long logId, CancellationToken ct = default)
    {
        var window = await GetWindowAsync(courtId, logId, ct);
        MaintenanceStateMachine.EnsureCanTransition(window.Status, MaintenanceStatus.Completed);

        window.Status = MaintenanceStatus.Completed;
        window.EndUtc = _clock.UtcNow; // "Fix": maintenance done now → court becomes available
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Completed (fixed) maintenance window {LogId} on court {CourtId}.", logId, courtId);
        return await ProjectAsync(logId, ct);
    }

    public async Task<CourtMaintenanceLogDto> CancelAsync(long courtId, long logId, CancellationToken ct = default)
    {
        var window = await GetWindowAsync(courtId, logId, ct);
        MaintenanceStateMachine.EnsureCanTransition(window.Status, MaintenanceStatus.Cancelled);

        var wasInProgress = window.Status == MaintenanceStatus.InProgress;
        window.Status = MaintenanceStatus.Cancelled;
        // An in-progress window that is called off ends now; a not-yet-started (Scheduled) one keeps its planned end
        // (it is terminal anyway, so it no longer counts as open).
        if (wasInProgress)
        {
            window.EndUtc = _clock.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cancelled maintenance window {LogId} on court {CourtId}.", logId, courtId);
        return await ProjectAsync(logId, ct);
    }

    public async Task<bool> IsCourtUnderMaintenanceAsync(long courtId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return await _db.CourtMaintenanceLogs.AsNoTracking().AnyAsync(
            m => m.CourtId == courtId
                 && (m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress)
                 && m.StartUtc <= now
                 && (m.EndUtc == null || m.EndUtc > now),
            ct);
    }

    public async Task<IReadOnlyList<long>> GetCourtIdsUnderMaintenanceAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        // Open windows overlapping [fromUtc, toUtc): start before the range ends AND (open-ended OR ends after it
        // begins). Distinct court ids — the reusable exclusion set for booking (F13) and analytics (F19).
        return await _db.CourtMaintenanceLogs.AsNoTracking()
            .Where(m => (m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress)
                        && m.StartUtc < toUtc
                        && (m.EndUtc == null || m.EndUtc > fromUtc))
            .Select(m => m.CourtId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>True when an open (Scheduled/InProgress) window — optionally excluding <paramref name="excludeLogId"/>
    /// — overlaps <c>[startUtc, endUtc)</c>, treating a null end as open-ended (+∞).</summary>
    private async Task<bool> OverlapsOpenWindowAsync(
        long courtId, DateTime startUtc, DateTime? endUtc, long? excludeLogId, CancellationToken ct)
    {
        var query = _db.CourtMaintenanceLogs.AsNoTracking()
            .Where(m => m.CourtId == courtId
                        && (m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress));
        if (excludeLogId.HasValue)
        {
            query = query.Where(m => m.Id != excludeLogId.Value);
        }

        // Two windows overlap iff aStart < bEnd && bStart < aEnd. The new window's end may be open-ended (null), in
        // which case only the existing end matters.
        if (endUtc.HasValue)
        {
            var newEnd = endUtc.Value;
            return await query.AnyAsync(m => m.StartUtc < newEnd && (m.EndUtc == null || m.EndUtc > startUtc), ct);
        }

        return await query.AnyAsync(m => m.EndUtc == null || m.EndUtc > startUtc, ct);
    }

    /// <summary>Loads the tracked window for a transition; a missing window (or wrong court) is a clean 404.</summary>
    private async Task<Domain.Entities.CourtMaintenanceLog> GetWindowAsync(
        long courtId, long logId, CancellationToken ct)
    {
        await EnsureCourtExistsAsync(courtId, ct);
        return await _db.CourtMaintenanceLogs.FirstOrDefaultAsync(m => m.Id == logId && m.CourtId == courtId, ct)
            ?? throw new NotFoundException($"Maintenance window {logId} was not found on court {courtId}.");
    }

    /// <summary>Verifies the court exists so a missing court is a clean 404, not an empty history.</summary>
    private async Task EnsureCourtExistsAsync(long courtId, CancellationToken ct)
    {
        if (!await _db.Courts.AnyAsync(c => c.Id == courtId, ct))
        {
            throw new NotFoundException($"Court {courtId} was not found.");
        }
    }

    /// <summary>Re-reads one window with the actor name JOINed (single query) so writes return the same shape as
    /// the read path.</summary>
    private async Task<CourtMaintenanceLogDto> ProjectAsync(long id, CancellationToken ct) =>
        ToDto(await Project(_db.CourtMaintenanceLogs.AsNoTracking().Where(m => m.Id == id)).FirstAsync(ct));

    /// <summary>Projection shared by the history list and the post-write re-read: JOINs the actor for name/email
    /// (no N+1). <c>StatusName</c> and the final <c>PerformedByName</c> are resolved in memory by <see cref="ToDto"/>
    /// so the SQL stays provider-agnostic.</summary>
    private static IQueryable<LogRow> Project(IQueryable<Domain.Entities.CourtMaintenanceLog> source) =>
        source.Select(m => new LogRow(
            m.Id,
            m.CourtId,
            m.Status,
            m.Reason,
            m.StartUtc,
            m.EndUtc,
            m.PerformedBy != null ? m.PerformedBy.FirstName + " " + m.PerformedBy.LastName : null,
            m.PerformedBy != null ? m.PerformedBy.Email : null,
            m.CreatedAtUtc));

    private static CourtMaintenanceLogDto ToDto(LogRow r)
    {
        var fullName = string.IsNullOrWhiteSpace(r.PerformedByName) ? null : r.PerformedByName.Trim();
        var performedBy = !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : string.IsNullOrWhiteSpace(r.PerformedByEmail) ? null : r.PerformedByEmail;

        return new CourtMaintenanceLogDto(
            r.Id, r.CourtId, r.Status, StatusLabel(r.Status), r.Reason, r.StartUtc, r.EndUtc, performedBy,
            r.CreatedAtUtc);
    }

    /// <summary>The human-readable status label sent on the wire (so the client never has to map the raw enum).</summary>
    private static string StatusLabel(MaintenanceStatus status) => status switch
    {
        MaintenanceStatus.Scheduled => "Scheduled",
        MaintenanceStatus.InProgress => "In Progress",
        MaintenanceStatus.Completed => "Completed",
        MaintenanceStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    /// <summary>Provider-agnostic intermediate carrying the scalar window fields plus the actor's name/email; the
    /// final <c>StatusName</c>/<c>PerformedByName</c> are built in memory by <see cref="ToDto"/>.</summary>
    private sealed record LogRow(
        long Id,
        long CourtId,
        MaintenanceStatus Status,
        string Reason,
        DateTime StartUtc,
        DateTime? EndUtc,
        string? PerformedByName,
        string? PerformedByEmail,
        DateTime CreatedAtUtc);
}
