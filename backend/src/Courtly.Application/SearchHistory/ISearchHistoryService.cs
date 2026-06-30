using Courtly.Contracts.SearchHistory;

namespace Courtly.Application.SearchHistory;

/// <summary>
/// Captures a user's court search as a recommender signal (feature 23). The controller only model-binds and calls
/// <see cref="RecordAsync"/>; the owner is taken from the JWT (never the body) and the timestamp is stamped here. The
/// write is best-effort: when the caller is unauthenticated nothing is recorded, and an identical search repeated
/// inside a short dedupe window is dropped rather than inserted. Returns nothing — the entity never leaves the layer.
/// </summary>
public interface ISearchHistoryService
{
    /// <summary>Records the search for the current user. No-op when unauthenticated or when an identical row was
    /// written within the dedupe window.</summary>
    Task RecordAsync(RecordSearchRequest request, CancellationToken ct = default);
}
