using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Validation;
using Courtly.Application.Users;
using Courtly.Contracts.Common;
using Courtly.Contracts.Users;
using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Users;

/// <summary>
/// Feature 15 slice: the admin user lookup behind "+ New Booking". Mirrors the reference-service in-memory harness
/// (EF InMemory, fresh DB per test). The search test relies on <see cref="UserService"/>'s provider guard — under
/// InMemory it uses a lowered <c>Contains</c> instead of the Postgres-only <c>EF.Functions.ILike</c>, so the same
/// test exercises case-insensitive name/email filtering. Pagination is clamped server-side (rubric §8.2).
///
/// Feature 15A: detail, activate/deactivate and role assignment. These need real Identity + seeded roles, so they run
/// over an Identity-backed harness (mirrors <c>AuthTestHarness</c>: AddIdentityCore + AddRoles + EnsureCreatedAsync
/// seeds Admin/Staff/User) with a fake <see cref="ICurrentUser"/> for the acting-admin self-guards.
/// </summary>
public class UserServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .Options);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? Email => null;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAtUtc => null;
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    /// <summary>
    /// Real <see cref="UserManager{AppUser}"/> over the EF in-memory provider (IdentityCore, no host), sharing one
    /// <see cref="CourtlyDbContext"/> with the <see cref="UserService"/> under test. <c>EnsureCreatedAsync</c> applies
    /// the seeded Admin/Staff/User roles so role assignment works end-to-end.
    /// </summary>
    private sealed class UserHarness : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public CourtlyDbContext Db { get; }
        public UserManager<AppUser> UserManager { get; }

        private UserHarness(ServiceProvider provider, CourtlyDbContext db, UserManager<AppUser> userManager)
        {
            Provider = provider;
            Db = db;
            UserManager = userManager;
        }

        public static async Task<UserHarness> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddDbContext<CourtlyDbContext>(o => o.UseInMemoryDatabase($"users-{Guid.NewGuid()}"));
            services
                .AddIdentityCore<AppUser>(o => o.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<CourtlyDbContext>();

            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<CourtlyDbContext>();
            await db.Database.EnsureCreatedAsync(); // applies HasData roles (Admin/Staff/User)

            var userManager = provider.GetRequiredService<UserManager<AppUser>>();
            return new UserHarness(provider, db, userManager);
        }

        /// <summary>Creates a persisted user in the given role, returning their id. Pass <paramref name="id"/> to give
        /// the user a fixed key (e.g. the seeded super-admin id); otherwise EF generates one.</summary>
        public async Task<Guid> AddUserAsync(
            string first, string last, string email, string role,
            bool isActive = true, long? cityId = null, Guid? id = null)
        {
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FirstName = first,
                LastName = last,
                CityId = cityId,
                IsActive = isActive,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            if (id is not null)
            {
                user.Id = id.Value;
            }
            var created = await UserManager.CreateAsync(user);
            Assert.True(created.Succeeded);
            await UserManager.AddToRoleAsync(user, role);
            return user.Id;
        }

        public UserService ServiceAs(Guid? actingUserId) =>
            new(Db, UserManager, new TestCurrentUser { UserId = actingUserId });

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static UserService NewService(CourtlyDbContext db) =>
        new(db, userManager: null!, currentUser: new TestCurrentUser());

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

    [Fact]
    public async Task GetByIdAsync_returns_roles_and_resolved_fields()
    {
        await using var harness = await UserHarness.CreateAsync();
        var city = new City { Name = "Sarajevo", Country = new Country { Name = "Bosnia", IsoCode = "BIH" } };
        harness.Db.Cities.Add(city);
        await harness.Db.SaveChangesAsync();
        var id = await harness.AddUserAsync("Alice", "Marsh", "alice@courtly.test", Roles.Staff, cityId: city.Id);

        var detail = await harness.ServiceAs(actingUserId: null).GetByIdAsync(id);

        Assert.Equal(id, detail.Id);
        Assert.Equal("Alice", detail.FirstName);
        Assert.Equal("Marsh", detail.LastName);
        Assert.Equal("Alice Marsh", detail.FullName);
        Assert.Equal("alice@courtly.test", detail.Email);
        Assert.Equal("Sarajevo", detail.CityName);
        Assert.Equal(new[] { Roles.Staff }, detail.Roles);
        Assert.True(detail.IsActive);
    }

    [Fact]
    public async Task GetByIdAsync_throws_when_user_missing()
    {
        await using var harness = await UserHarness.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.ServiceAs(actingUserId: null).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetActiveAsync_deactivates_and_persists()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);
        var target = await harness.AddUserAsync("Target", "User", "target@courtly.test", Roles.User);

        var detail = await harness.ServiceAs(actingUserId: admin).SetActiveAsync(target, isActive: false);

        Assert.False(detail.IsActive);
        // Re-read from the store confirms the change persisted, not just the returned DTO.
        var reread = await harness.UserManager.FindByIdAsync(target.ToString());
        Assert.False(reread!.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_rejects_self_deactivation()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);

        await Assert.ThrowsAsync<BusinessException>(
            () => harness.ServiceAs(actingUserId: admin).SetActiveAsync(admin, isActive: false));
    }

    [Fact]
    public async Task SetActiveAsync_throws_when_user_missing()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.ServiceAs(actingUserId: admin).SetActiveAsync(Guid.NewGuid(), isActive: false));
    }

    [Fact]
    public async Task AssignRoleAsync_replaces_the_users_role()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);
        var target = await harness.AddUserAsync("Target", "User", "target@courtly.test", Roles.User);

        var detail = await harness.ServiceAs(actingUserId: admin).AssignRoleAsync(target, Roles.Staff);

        Assert.Equal(new[] { Roles.Staff }, detail.Roles);
        // GetRolesAsync reflects the single-role replacement (old User role removed).
        var user = await harness.UserManager.FindByIdAsync(target.ToString());
        var roles = await harness.UserManager.GetRolesAsync(user!);
        Assert.Equal(new[] { Roles.Staff }, roles);
    }

    [Fact]
    public async Task AssignRoleAsync_rejects_self_role_change()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);

        await Assert.ThrowsAsync<BusinessException>(
            () => harness.ServiceAs(actingUserId: admin).AssignRoleAsync(admin, Roles.Staff));
    }

    [Fact]
    public void AssignRoleRequestValidator_rejects_an_unknown_role()
    {
        var validator = new AssignRoleRequestValidator();

        var result = validator.Validate(new AssignRoleRequest("Superuser"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignRoleRequest.Role));
    }

    [Fact]
    public async Task SetActiveAsync_cannot_deactivate_the_super_admin()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);
        var super = await harness.AddUserAsync(
            "Super", "Admin", "super@courtly.test", Roles.Admin, id: SeedIds.UserAdmin);

        // A *different* admin (self-guard doesn't apply) still cannot deactivate the protected super admin.
        await Assert.ThrowsAsync<BusinessException>(
            () => harness.ServiceAs(actingUserId: admin).SetActiveAsync(super, isActive: false));
    }

    [Fact]
    public async Task SetActiveAsync_allows_reactivating_the_super_admin()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);
        var super = await harness.AddUserAsync(
            "Super", "Admin", "super@courtly.test", Roles.Admin, isActive: false, id: SeedIds.UserAdmin);

        var detail = await harness.ServiceAs(actingUserId: admin).SetActiveAsync(super, isActive: true);

        Assert.True(detail.IsActive); // the guard blocks deactivation only, not recovery.
    }

    [Fact]
    public async Task AssignRoleAsync_cannot_change_the_super_admin_role()
    {
        await using var harness = await UserHarness.CreateAsync();
        var admin = await harness.AddUserAsync("Admin", "One", "admin@courtly.test", Roles.Admin);
        var super = await harness.AddUserAsync(
            "Super", "Admin", "super@courtly.test", Roles.Admin, id: SeedIds.UserAdmin);

        await Assert.ThrowsAsync<BusinessException>(
            () => harness.ServiceAs(actingUserId: admin).AssignRoleAsync(super, Roles.Staff));
    }

    [Fact]
    public async Task GetByIdAsync_flags_only_the_super_admin_as_protected()
    {
        await using var harness = await UserHarness.CreateAsync();
        var super = await harness.AddUserAsync(
            "Super", "Admin", "super@courtly.test", Roles.Admin, id: SeedIds.UserAdmin);
        var regular = await harness.AddUserAsync("Reg", "User", "reg@courtly.test", Roles.User);

        Assert.True((await harness.ServiceAs(actingUserId: null).GetByIdAsync(super)).IsProtected);
        Assert.False((await harness.ServiceAs(actingUserId: null).GetByIdAsync(regular)).IsProtected);
    }
}
