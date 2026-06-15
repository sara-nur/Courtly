using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Courtly.Tests.Auth;

/// <summary>Settable clock so token expiry / single-use windows are deterministic.</summary>
internal sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}

/// <summary>Captures the raw reset token the service "emails", so tests can assert it hashes to the stored row.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public int SendCount { get; private set; }
    public string? LastEmail { get; private set; }
    public string? LastToken { get; private set; }

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        SendCount++;
        LastEmail = toEmail;
        LastToken = resetToken;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Builds a real <see cref="UserManager{AppUser}"/> over the EF in-memory provider (IdentityCore, no host)
/// wired to a <see cref="AuthService"/> + <see cref="TokenService"/> sharing one <see cref="CourtlyDbContext"/>.
/// </summary>
internal sealed class AuthHarness : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public CourtlyDbContext Db { get; }
    public UserManager<AppUser> UserManager { get; }
    public TestClock Clock { get; }
    public RecordingEmailSender Email { get; }
    public TokenService TokenService { get; }
    public AuthService Auth { get; }

    private AuthHarness(
        ServiceProvider provider,
        CourtlyDbContext db,
        UserManager<AppUser> userManager,
        TestClock clock,
        RecordingEmailSender email,
        TokenService tokenService,
        AuthService auth)
    {
        Provider = provider;
        Db = db;
        UserManager = userManager;
        Clock = clock;
        Email = email;
        TokenService = tokenService;
        Auth = auth;
    }

    public static JwtOptions DefaultJwt() => new()
    {
        Key = "courtly-test-signing-key-0123456789-abcdef",
        Issuer = "courtly-test",
        Audience = "courtly-test",
        AccessMinutes = 15,
        RefreshDays = 7,
        ResetTokenMinutes = 60,
    };

    public static async Task<AuthHarness> CreateAsync(JwtOptions? jwt = null)
    {
        var clock = new TestClock();
        var email = new RecordingEmailSender();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMemoryCache();
        services.AddDbContext<CourtlyDbContext>(o => o.UseInMemoryDatabase($"auth-{Guid.NewGuid()}"));
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
        await db.Database.EnsureCreatedAsync(); // applies HasData roles (Admin/Staff/User)

        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var tokenService = new TokenService(Options.Create(jwt ?? DefaultJwt()), clock);
        var revokedTokenCache = new RevokedTokenCache(provider.GetRequiredService<IMemoryCache>(), db, clock);
        var auth = new AuthService(
            userManager, db, tokenService, email, clock, revokedTokenCache, NullLogger<AuthService>.Instance);

        return new AuthHarness(provider, db, userManager, clock, email, tokenService, auth);
    }

    public ValueTask DisposeAsync() => Provider.DisposeAsync();
}
