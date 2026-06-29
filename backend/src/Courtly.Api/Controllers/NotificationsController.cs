using Courtly.Application.Notifications;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// The caller's in-app notification inbox (feature 18). A thin controller over <see cref="INotificationService"/>: it
/// model-binds, calls the service, and returns the DTO — ownership, paging and read-state transitions live in the
/// service, never here (rubric §7). Every endpoint requires authentication and the owner is taken from the JWT (never
/// the route/body), so a caller can only ever see or mutate their own notifications. The service throws the app's custom
/// exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    /// <summary>The caller's own notifications, newest first (paginated).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> ListMine(
        [FromQuery] PaginationQuery pagination, CancellationToken ct)
        => Ok(await _notifications.ListMineAsync(pagination, ct));

    /// <summary>The caller's number of unread notifications (for a badge count).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct)
        => Ok(await _notifications.GetUnreadCountAsync(ct));

    /// <summary>Marks the caller's notification <paramref name="id"/> read (idempotent). 404 if it does not exist or
    /// belongs to someone else.</summary>
    [HttpPost("{id:long}/read")]
    public async Task<ActionResult<NotificationDto>> MarkAsRead(long id, CancellationToken ct)
        => Ok(await _notifications.MarkAsReadAsync(id, ct));

    /// <summary>Marks every one of the caller's unread notifications read.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        await _notifications.MarkAllAsReadAsync(ct);
        return NoContent();
    }
}
