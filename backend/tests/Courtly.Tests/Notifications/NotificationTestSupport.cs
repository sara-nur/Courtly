using Courtly.Application.Abstractions;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Tests.Notifications;

/// <summary>
/// Hand-rolled fakes for the feature-18 notification unit tests (no Moq in the repo — mirrors the auth/payments
/// harnesses). Each test spins up a fresh EF in-memory <see cref="CourtlyDbContext"/> (unique database name) so rows
/// never leak between tests, a fixed <see cref="IClock"/> so timestamps are deterministic, and an in-memory
/// <see cref="ICurrentUser"/> so owner-scoping is driven by the token (never the route/body).
/// </summary>
internal static class NotificationTestDb
{
    public static CourtlyDbContext New() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options);
}

/// <summary>Fixed UTC clock so <c>CreatedAtUtc</c>/<c>ReadAtUtc</c> are deterministic.</summary>
internal sealed class TestClock : IClock
{
    public DateTime UtcNow { get; init; } = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
}

/// <summary>An in-memory current-user; ownership flows from <see cref="UserId"/> (the JWT), never the route/body.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public string? Email => null;
    public string? Jti => null;
    public DateTime? AccessTokenExpiresAtUtc => null;
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool IsInRole(string role) => Roles.Contains(role);
}
