using Courtly.Application.Auth;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Courtly.Tests.Auth;

/// <summary>
/// A <see cref="SaveChangesInterceptor"/> that throws on the Nth save so a multi-save flow fails partway,
/// exercising the explicit-transaction rollback. Disabled until <see cref="FailOnSave"/> is set (1-based);
/// counts each <c>SavingChanges</c>/<c>SavingChangesAsync</c> from the point it is armed.
/// </summary>
internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>1-based ordinal of the save to fail on, or <c>null</c> to let every save through.</summary>
    public int? FailOnSave { get; set; }

    private int _saveCount;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfArmed();
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ThrowIfArmed();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ThrowIfArmed()
    {
        if (FailOnSave is null)
        {
            return;
        }

        _saveCount++;
        if (_saveCount == FailOnSave)
        {
            throw new InvalidOperationException($"Injected failure on save #{_saveCount}.");
        }
    }
}

/// <summary>
/// Sibling of <see cref="AuthHarness"/> that swaps the EF in-memory provider for SQLite over a kept-alive
/// <c>:memory:</c> connection. SQLite reports as relational, so the atomicity tests hit the real explicit
/// transaction (InMemory silently no-ops it). A <see cref="FailingSaveInterceptor"/> lets a test fail a chosen
/// save mid-flow and assert the whole flow rolled back.
/// </summary>
internal sealed class AuthSqliteHarness : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public CourtlyDbContext Db { get; }
    public UserManager<AppUser> UserManager { get; }
    public TestClock Clock { get; }
    public RecordingEmailSender Email { get; }
    public TokenService TokenService { get; }
    public AuthService Auth { get; }
    public FailingSaveInterceptor Interceptor { get; }

    private readonly SqliteConnection _connection;

    private AuthSqliteHarness(
        ServiceProvider provider,
        CourtlyDbContext db,
        UserManager<AppUser> userManager,
        TestClock clock,
        RecordingEmailSender email,
        TokenService tokenService,
        AuthService auth,
        FailingSaveInterceptor interceptor,
        SqliteConnection connection)
    {
        Provider = provider;
        Db = db;
        UserManager = userManager;
        Clock = clock;
        Email = email;
        TokenService = tokenService;
        Auth = auth;
        Interceptor = interceptor;
        _connection = connection;
    }

    public static async Task<AuthSqliteHarness> CreateAsync(JwtOptions? jwt = null)
    {
        var clock = new TestClock();
        var email = new RecordingEmailSender();
        var interceptor = new FailingSaveInterceptor();

        // A :memory: database lives only as long as its connection is open, so keep one open for the harness's
        // lifetime — otherwise EnsureCreated's schema would vanish before the first service call.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMemoryCache();
        services.AddDbContext<CourtlyDbContext>(o => o.UseSqlite(connection).AddInterceptors(interceptor));
        services
            .AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CourtlyDbContext>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<CourtlyDbContext>();
        await db.Database.EnsureCreatedAsync(); // builds the schema and applies HasData roles (Admin/Staff/User)

        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var tokenService = new TokenService(Options.Create(jwt ?? AuthHarness.DefaultJwt()), clock);
        var revokedTokenCache = new RevokedTokenCache(provider.GetRequiredService<IMemoryCache>(), db, clock);
        var auth = new AuthService(
            userManager, db, tokenService, email, clock, revokedTokenCache, NullLogger<AuthService>.Instance);

        return new AuthSqliteHarness(
            provider, db, userManager, clock, email, tokenService, auth, interceptor, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
