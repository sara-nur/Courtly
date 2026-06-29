using Courtly.Api.Realtime;
using Courtly.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Courtly.Api.Controllers;

/// <summary>
/// The Worker -> API real-time push endpoint (feature 18). Not a user-facing API: it is guarded by the
/// <c>"InternalKey"</c> policy (the shared <c>X-Internal-Key</c> secret, no JWT) and exists only so the JWT-less Worker
/// can fan a just-persisted notification out to its owner's live SignalR connections. It does not touch the database —
/// it forwards the already-built <see cref="NotificationDto"/> to the user's group via <see cref="NotificationHub"/>.
/// </summary>
[ApiController]
[Route("api/internal")]
[Authorize(Policy = "InternalKey")]
public sealed class InternalPushController : ControllerBase
{
    private readonly IHubContext<NotificationHub> _hub;

    public InternalPushController(IHubContext<NotificationHub> hub)
    {
        _hub = hub;
    }

    /// <summary>Pushes <paramref name="request"/>'s notification to every live connection of its owner (the user's
    /// SignalR group, keyed by user id). No-ops harmlessly when the user has no open connection.</summary>
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] InternalPushRequest request, CancellationToken ct)
    {
        await _hub.Clients.Group(request.UserId.ToString()).SendAsync("notification", request.Notification, ct);
        return NoContent();
    }
}
