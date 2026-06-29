using Courtly.Contracts.Notifications;

namespace Courtly.Worker.Internal;

/// <summary>
/// Client for the API's internal SignalR push endpoint (feature 18). After the notification consumer persists a row it
/// pushes the just-created <see cref="NotificationDto"/> to the API, which fans it out over SignalR to the owner's
/// connected clients. The push is best-effort: the row is already persisted and REST polling is the fallback, so the
/// Worker logs and swallows a push failure rather than re-persisting.
/// </summary>
public interface IInternalPushClient
{
    /// <summary>Posts <paramref name="request"/> to the API's internal push endpoint, authenticated by the shared
    /// internal key configured on the underlying <c>HttpClient</c>.</summary>
    Task PushAsync(InternalPushRequest request, CancellationToken ct = default);
}
