using Courtly.Application.Common.Pagination;
using Courtly.Contracts.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Pagination;

/// <summary>Feature 6 DoD (auto): pageSize is clamped to the max (100) and the page math is correct.</summary>
public class PaginationTests
{
    private sealed class Item
    {
        public int Id { get; set; }
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private static async Task<TestDb> SeedAsync(int count)
    {
        var options = new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase($"page-{Guid.NewGuid()}")
            .Options;
        var db = new TestDb(options);
        db.Items.AddRange(Enumerable.Range(1, count).Select(i => new Item { Id = i }));
        await db.SaveChangesAsync();
        return db;
    }

    [Theory]
    [InlineData(1, 20, 1, 20)]    // already valid
    [InlineData(0, 20, 1, 20)]    // page < 1 → 1
    [InlineData(-5, 20, 1, 20)]
    [InlineData(1, 0, 1, 20)]     // pageSize < 1 → default (20)
    [InlineData(1, 500, 1, 100)]  // pageSize > max → 100
    [InlineData(3, 101, 3, 100)]
    public void Normalize_clamps_page_and_pageSize(int page, int pageSize, int expectedPage, int expectedSize)
    {
        var normalized = new PaginationQuery { Page = page, PageSize = pageSize }.Normalize();

        Assert.Equal(expectedPage, normalized.Page);
        Assert.Equal(expectedSize, normalized.PageSize);
    }

    [Fact]
    public async Task ToPagedResultAsync_returns_the_requested_window_and_totals()
    {
        await using var db = await SeedAsync(25);

        var page = await db.Items.OrderBy(i => i.Id)
            .ToPagedResultAsync(new PaginationQuery { Page = 2, PageSize = 10 });

        Assert.Equal(25, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(11, page.Items[0].Id); // second page of 10 starts at item 11
        Assert.True(page.HasNext);
        Assert.True(page.HasPrevious);
    }

    [Fact]
    public async Task ToPagedResultAsync_clamps_oversized_pageSize_to_the_max()
    {
        await using var db = await SeedAsync(25);

        var page = await db.Items.OrderBy(i => i.Id)
            .ToPagedResultAsync(new PaginationQuery { Page = 1, PageSize = 500 });

        Assert.Equal(PaginationQuery.MaxPageSize, page.PageSize); // 500 → 100, never unbounded
        Assert.Equal(25, page.Items.Count);
        Assert.False(page.HasNext);
    }
}
