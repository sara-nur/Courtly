using Courtly.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Courtly.Api.Realtime;

/// <summary>
/// The real-time notification channel (feature 18). Every connection is authenticated (JWT carried in the
/// <c>access_token</c> query string, since browsers cannot set headers on the WebSocket handshake) and joins a SignalR
/// group keyed by its own user id (<see cref="NotificationGroups"/>). The internal push endpoint then fans a
/// just-persisted notification out to that group, so the server pushes the <c>"notification"</c> method carrying a
/// <see cref="Courtly.Contracts.Notifications.NotificationDto"/> to every live connection the user has open. Group
/// membership is derived solely from the principal — a connection can never be placed in another user's group.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>Places the connection in its owner's group, or aborts it when the principal carries no usable user id
    /// (defence in depth — <c>[Authorize]</c> already rejects unauthenticated connections).</summary>
    public override async Task OnConnectedAsync()
    {
        var group = NotificationGroups.For(Context.User);
        if (group is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }
}
