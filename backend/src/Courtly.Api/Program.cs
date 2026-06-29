// Courtly API host. Loads .env, binds typed options, registers the EF context + Identity + JWT auth and the
// feature-6 cross-cutting stack (exception middleware, validation pipeline, memory cache, CORS-once,
// current-user accessor), applies migrations + seeds on startup, and exposes the auth API behind Swagger.
using System.IdentityModel.Tokens.Jwt;
using Courtly.Api.Authorization;
using Courtly.Api.Identity;
using Courtly.Api.Middleware;
using Courtly.Api.Realtime;
using Courtly.Api.Validation;
using Courtly.Application.Abstractions;
using Courtly.Application.Common.Validation;
using Courtly.Application.DependencyInjection;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.DependencyInjection;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

const string CorsPolicy = "CourtlyCors";

EnvironmentLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);

var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required.");
builder.Services.AddCourtlyPersistence(dbConnectionString);
builder.Services.AddCourtlySeeding();

builder.Services.AddHealthChecks();

// --- Feature 6 cross-cutting infrastructure ---
builder.Services.AddMemoryCache();                          // hot reads / jti denylist cache (rubric §8.2)
builder.Services.AddHttpContextAccessor();                  // backs ICurrentUser (rubric §3.4) — added once
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddCourtlyCrossCutting();                  // IRevokedTokenCache
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// One CORS policy with an explicit origin allow-list from .env (rubric §3.4: configure once, never allow-any).
var corsOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    }));

// Controllers + the single validation gate. Suppress [ApiController]'s automatic 400 so every validation
// error flows through ValidationActionFilter → ExceptionHandlingMiddleware → standardized ErrorResponse.
builder.Services.AddControllers(options => options.Filters.Add<ValidationActionFilter>());
builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Paste the JWT access token (without the 'Bearer ' prefix).",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
    });
    // Swashbuckle 10 / OpenApi v2: requirement is a factory; reference the definition against the document.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document, null), new List<string>() },
    });
});

// AddIdentityCore (not AddIdentity): a JWT API has no cookies — AddIdentity would register the cookie
// handler and hijack the default scheme, turning [Authorize] 401s into login-page redirects.
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<CourtlyDbContext>();

builder.Services.AddCourtlyAuth();
builder.Services.AddCourtlyReferenceData();   // feature 9 reference-data CRUD services
builder.Services.AddCourtlyCourtCatalog();    // feature 10 court catalog CRUD services
builder.Services.AddCourtlyReservations();    // feature 14 reservation engine + state machine
builder.Services.AddCourtlyUsers();           // feature 15 admin user lookup (15A extends)
builder.Services.AddCourtlyPayments();        // feature 16 Stripe payments (intent + webhook + refund)
builder.Services.AddRabbitMqConnection();     // F17: shared singleton RabbitMQ connection
builder.Services.AddRabbitMqMessaging();      // F17: real publisher REPLACES the logging stubs (rubric §3.2 — main service publishes to RabbitMQ)
builder.Services.AddCourtlyNotificationService(); // F18: user-facing in-app notification inbox
builder.Services.AddSignalR();                // F18: real-time notification push hub

// F18: stateless verifier for the "InternalKey" policy — the Worker authenticates to /api/internal/push on the
// shared X-Internal-Key secret alone (no JWT). Singleton: it holds no per-request state.
builder.Services.AddSingleton<IAuthorizationHandler, InternalKeyAuthorizationHandler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configure JwtBearer from the single source of truth (ITokenService) + the jti revocation denylist.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<ITokenService>((options, tokenService) =>
    {
        // Keep claim names exactly as minted so jti/nameidentifier/role reads are deterministic.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.BuildValidationParameters();
        options.Events = new JwtBearerEvents
        {
            // Reject access tokens whose jti was revoked at logout. F6 serves this denylist from IMemoryCache.
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (string.IsNullOrEmpty(jti))
                {
                    context.Fail("Token is missing the jti claim.");
                    return;
                }

                // Resolve the scoped cache from the request scope — never capture it in this lambda.
                var revokedCache = context.HttpContext.RequestServices.GetRequiredService<IRevokedTokenCache>();
                if (await revokedCache.IsRevokedAsync(jti, context.HttpContext.RequestAborted))
                {
                    context.Fail("Token has been revoked.");
                }
            },
            // F18: WebSocket handshakes cannot set an Authorization header, so the SignalR client passes the JWT in
            // the access_token query string. Lift it onto context.Token only for the notifications hub path.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

// F18: "InternalKey" gates the Worker -> API push endpoint. It deliberately does NOT RequireAuthenticatedUser — the
// JWT-less Worker passes on the shared X-Internal-Key secret alone (verified by InternalKeyAuthorizationHandler).
builder.Services.AddAuthorization(options =>
    options.AddPolicy("InternalKey", policy => policy.AddRequirements(new InternalKeyRequirement())));

var app = builder.Build();

// Apply EF migrations (which insert the HasData reference rows) then run the idempotent runtime seeder, so
// `docker compose up` brings the 200067 schema up to date and populates demo data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CourtlyDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<ICourtlyDataSeeder>();
    await seeder.SeedAsync();
}

// Exception boundary first so it wraps the whole pipeline and never leaks a stack trace (rubric §3.4).
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Courtly API");
app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");   // F18: real-time notification channel

app.Run();
