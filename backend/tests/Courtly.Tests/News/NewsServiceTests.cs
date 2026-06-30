using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.News;
using Courtly.Contracts.Common;
using Courtly.Contracts.News;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.News;

/// <summary>
/// Feature 21 DoD (auto): News CRUD + validation. Mirrors the golden Feature 9/10 service harness (EF InMemory
/// provider, fresh DB per test, NullLogger, fixed clock). The search test relies on <see cref="NewsService"/>'s
/// provider guard — under InMemory it uses a lowered <c>Contains</c> instead of the Postgres-only
/// <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive Title/Text filtering. Image validation is
/// exercised through the shared <c>ImageContentValidator</c> (valid PNG magic bytes vs unsupported bytes).
/// </summary>
public class NewsServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"news-{Guid.NewGuid()}")
            .Options);

    /// <summary>A fixed clock so the published-feed "publish time has arrived" filter is deterministic.</summary>
    private static readonly DateTime FixedNow = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public TestClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; }
    }

    private static NewsService NewService(CourtlyDbContext db, DateTime? now = null) =>
        new(db, new TestClock(now ?? FixedNow), NullLogger<NewsService>.Instance);

    // Minimal valid magic-byte payloads the shared ImageContentValidator accepts.
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02 };
    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A };

    private static async Task<Guid> SeedAuthorAsync(CourtlyDbContext db, string first = "Super", string last = "Admin")
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last}@courtly.test".ToLowerInvariant(),
            UserName = $"{first}.{last}@courtly.test".ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static CreateNewsRequest NewRequest(
        string title = "Season Opener", string text = "The courts are now open.",
        DateTime? publishedAt = null, bool isActive = true) =>
        new(title, text, publishedAt ?? FixedNow.AddDays(-1), isActive);

    // --- Create + image validation ---------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto_with_author_name_and_image_url()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db, "Super", "Admin");
        var service = NewService(db);

        var dto = await service.CreateAsync(
            NewRequest(title: "  Grand Opening  ", text: "  Welcome to the season!  "),
            PngBytes, "image/png", authorId);

        Assert.True(dto.Id > 0);
        Assert.Equal("Grand Opening", dto.Title);              // trimmed
        Assert.Equal("Welcome to the season!", dto.Text);      // trimmed
        Assert.Equal($"/api/news/{dto.Id}/image", dto.ImageUrl);
        Assert.Equal("Super Admin", dto.AuthorName);           // projected via JOIN, not stored
        Assert.True(dto.IsActive);
        Assert.Equal(1, await db.News.CountAsync());

        var stored = await db.News.AsNoTracking().FirstAsync();
        Assert.Equal("image/png", stored.ImageContentType);    // normalized by the validator
        Assert.Equal(authorId, stored.AuthorId);               // author from the JWT arg, not the body
        Assert.Equal(DateTimeKind.Utc, stored.PublishedAtUtc.Kind);
    }

    [Fact]
    public async Task CreateAsync_with_unsupported_image_throws_Validation_and_writes_nothing()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(NewRequest(), new byte[] { 0x01, 0x02, 0x03, 0x04 }, "image/png", authorId));

        Assert.Equal(0, await db.News.CountAsync());
    }

    // --- List: search + active filter + ordering -------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_search_filters_by_title_or_text_case_insensitively()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        await service.CreateAsync(NewRequest(title: "Clay Court Tournament", text: "Join us"), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Membership Update", text: "New clay surfaces installed"), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Holiday Hours", text: "Closed Monday"), PngBytes, "image/png", authorId);

        var page = await service.GetPagedAsync(new PaginationQuery(), search: "CLAY", activeOnly: null);

        Assert.Equal(2, page.TotalCount); // one matches the title, one matches the text
    }

    [Fact]
    public async Task GetPagedAsync_activeOnly_filters_published_vs_hidden()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        await service.CreateAsync(NewRequest(title: "Visible", isActive: true), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Hidden", isActive: false), PngBytes, "image/png", authorId);

        var active = await service.GetPagedAsync(new PaginationQuery(), null, activeOnly: true);
        var hidden = await service.GetPagedAsync(new PaginationQuery(), null, activeOnly: false);
        var all = await service.GetPagedAsync(new PaginationQuery(), null, activeOnly: null);

        Assert.Equal("Visible", Assert.Single(active.Items).Title);
        Assert.Equal("Hidden", Assert.Single(hidden.Items).Title);
        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task GetPublishedAsync_returns_only_active_and_already_published_newest_first()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        await service.CreateAsync(NewRequest(title: "Old Active", publishedAt: FixedNow.AddDays(-5)), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Recent Active", publishedAt: FixedNow.AddDays(-1)), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Scheduled Future", publishedAt: FixedNow.AddDays(2)), PngBytes, "image/png", authorId);
        await service.CreateAsync(NewRequest(title: "Hidden Past", publishedAt: FixedNow.AddDays(-3), isActive: false), PngBytes, "image/png", authorId);

        var page = await service.GetPublishedAsync(new PaginationQuery());

        Assert.Equal(2, page.TotalCount);                    // future + hidden excluded
        Assert.Equal("Recent Active", page.Items[0].Title);  // newest published first
        Assert.Equal("Old Active", page.Items[1].Title);
    }

    [Fact]
    public async Task GetByIdAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(999));
    }

    // --- Image serving ---------------------------------------------------------------------------

    [Fact]
    public async Task GetImageContentAsync_returns_stored_bytes_or_null()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        var created = await service.CreateAsync(NewRequest(), PngBytes, "image/png", authorId);

        var content = await service.GetImageContentAsync(created.Id);
        Assert.NotNull(content);
        Assert.Equal("image/png", content!.Value.ContentType);
        Assert.Equal(PngBytes, content.Value.Bytes);

        Assert.Null(await service.GetImageContentAsync(999));
    }

    // --- Update (image optional-replace) ---------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_keeps_image_when_no_file_and_replaces_when_supplied()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        var created = await service.CreateAsync(NewRequest(title: "Original"), PngBytes, "image/png", authorId);

        // No new file → existing image preserved; text/active fields still update.
        var kept = await service.UpdateAsync(
            created.Id, new UpdateNewsRequest("Edited", "New body", FixedNow, false), null, null);
        Assert.Equal("Edited", kept.Title);
        Assert.False(kept.IsActive);
        var afterKeep = await db.News.AsNoTracking().FirstAsync(n => n.Id == created.Id);
        Assert.Equal("image/png", afterKeep.ImageContentType);
        Assert.Equal(PngBytes, afterKeep.ImageBytes);

        // New file → image replaced.
        await service.UpdateAsync(
            created.Id, new UpdateNewsRequest("Edited", "New body", FixedNow, true), JpegBytes, "image/jpeg");
        var afterReplace = await db.News.AsNoTracking().FirstAsync(n => n.Id == created.Id);
        Assert.Equal("image/jpeg", afterReplace.ImageContentType);
        Assert.Equal(JpegBytes, afterReplace.ImageBytes);
    }

    [Fact]
    public async Task UpdateAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.UpdateAsync(999, new UpdateNewsRequest("X", "Y", FixedNow, true), null, null));
    }

    // --- Delete ----------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_removes_the_row()
    {
        await using var db = NewDb();
        var authorId = await SeedAuthorAsync(db);
        var service = NewService(db);
        var created = await service.CreateAsync(NewRequest(), PngBytes, "image/png", authorId);

        await service.DeleteAsync(created.Id);

        Assert.Equal(0, await db.News.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }
}
