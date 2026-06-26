using Courtly.Application.Users;
using Courtly.Contracts.Common;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Users;

/// <summary>
/// Feature 15 slice: the admin user lookup behind "+ New Booking". Mirrors the reference-service in-memory harness
/// (EF InMemory, fresh DB per test). The search test relies on <see cref="UserService"/>'s provider guard — under
/// InMemory it uses a lowered <c>Contains</c> instead of the Postgres-only <c>EF.Functions.ILike</c>, so the same
/// test exercises case-insensitive name/email filtering. Pagination is clamped server-side (rubric §8.2).
/// </summary>
public class UserServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .Options);

    private static UserService NewService(CourtlyDbContext db) => new(db);

    private static AppUser User(string first, string last, string email, bool isActive = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Email = email,
            UserName = email,
            IsActive = isActive,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task SearchAsync_returns_users_ordered_by_name_with_resolved_full_name()
    {
        await using var db = NewDb();
        db.Users.AddRange(
            User("Bob", "Stone", "bob@courtly.test"),
            User("Alice", "Marsh", "alice@courtly.test"));
        await db.SaveChangesAsync();

        var page = await NewService(db).SearchAsync(new PaginationQuery(), search: null);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Alice Marsh", page.Items[0].FullName); // ordered by first name
        Assert.Equal("alice@courtly.test", page.Items[0].Email);
        Assert.True(page.Items[0].IsActive);
    }

    [Fact]
    public async Task SearchAsync_filters_by_name_or_email_case_insensitively()
    {
        await using var db = NewDb();
        db.Users.AddRange(
            User("Alice", "Marsh", "alice@courtly.test"),
            User("Bob", "Stone", "bob@courtly.test"),
            User("Carol", "Nguyen", "carol@example.com"));
        await db.SaveChangesAsync();
        var service = NewService(db);

        var byName = await service.SearchAsync(new PaginationQuery(), "ALICE");
        Assert.Equal("Alice Marsh", Assert.Single(byName.Items).FullName);

        var byEmail = await service.SearchAsync(new PaginationQuery(), "example.com");
        Assert.Equal("Carol Nguyen", Assert.Single(byEmail.Items).FullName);
    }

    [Fact]
    public async Task SearchAsync_clamps_an_oversized_page_size()
    {
        await using var db = NewDb();
        db.Users.Add(User("Solo", "User", "solo@courtly.test"));
        await db.SaveChangesAsync();

        var page = await NewService(db).SearchAsync(new PaginationQuery { PageSize = 500 }, search: null);

        Assert.Equal(PaginationQuery.MaxPageSize, page.PageSize);
    }
}
