using System.Security.Claims;

namespace Courtly.Application.Notifications;

/// <summary>
/// Maps an authenticated principal to its SignalR group name for real-time notification fan-out (feature 18). Each user
/// joins a group keyed by their own id, so the internal push endpoint can deliver a notification to every live
/// connection that user has open. Pure — reads only the <see cref="ClaimTypes.NameIdentifier"/> claim (the same claim
/// the API's <c>CurrentUser</c> resolves the user id from), so a connection can never be placed in another user's group.
/// </summary>
public static class NotificationGroups
{
    /// <summary>The group name (the user-id string) for <paramref name="user"/>, or null when the principal is absent
    /// or carries no usable <see cref="ClaimTypes.NameIdentifier"/> claim. The id string is used verbatim as the group
    /// key so both the hub (join) and the push endpoint (send) derive the same group.</summary>
    public static string? For(ClaimsPrincipal? user)
    {
        var id = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
