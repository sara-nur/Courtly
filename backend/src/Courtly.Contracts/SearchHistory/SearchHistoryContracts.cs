namespace Courtly.Contracts.SearchHistory;

/// <summary>
/// Search-history capture contracts (feature 23 recommender signal). The client posts the filters it just searched
/// with; the server stamps the owner (from the JWT, never the body) and the timestamp. DTOs only on the wire — the
/// service maps this onto the <c>SearchHistory</c> entity and never returns it. Server-side validation (price bounds,
/// raw-query length) lives in the FluentValidation validator, not here.
/// </summary>
public sealed record RecordSearchRequest(
    long? SurfaceTypeId,
    long? CourtTypeId,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? IndoorOnly,
    string? RawQuery);
