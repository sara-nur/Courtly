using System.Security.Claims;
using Courtly.Application.Notifications;
using Xunit;

namespace Courtly.Tests.Notifications;

/// <summary>
/// Feature 18 (auto): the SignalR group key is derived purely from the connection principal's
/// <see cref="ClaimTypes.NameIdentifier"/> — the same claim the API resolves the user id from — so a connection can
/// only ever join its own user's group (hub-authorization/membership rule, expressed without a live hub). A principal
/// with no usable id (missing claim, or a null principal) maps to no group.
/// </summary>
public class NotificationGroupsTests
{
    [Fact]
    public void For_PrincipalWithNameIdentifier_ReturnsThatIdString()
    {
        var id = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, authenticationType: "test"));

        Assert.Equal(id, NotificationGroups.For(principal));
    }

    [Fact]
    public void For_PrincipalWithoutNameIdentifier_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "user@example.com") }, authenticationType: "test"));

        Assert.Null(NotificationGroups.For(principal));
    }

    [Fact]
    public void For_NullPrincipal_ReturnsNull()
    {
        Assert.Null(NotificationGroups.For(null));
    }
}
