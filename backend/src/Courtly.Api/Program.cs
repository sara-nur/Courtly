// Courtly API host. Loads .env, binds typed options, registers the EF context + Identity + JWT auth,
// applies migrations + seeds data on startup, and exposes the auth API (feature 5) behind Swagger.
using System.IdentityModel.Tokens.Jwt;
using Courtly.Application.Abstractions;
using Courtly.Application.DependencyInjection;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

EnvironmentLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCourtlyOptions(builder.Configuration);

var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING is required.");
builder.Services.AddCourtlyPersistence(dbConnectionString);
builder.Services.AddCourtlySeeding();

builder.Services.AddHealthChecks();

builder.Services.AddControllers();
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
            // Reject access tokens whose jti was revoked at logout. F6 caches this denylist in IMemoryCache.
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (string.IsNullOrEmpty(jti))
                {
                    context.Fail("Token is missing the jti claim.");
                    return;
                }

                // Resolve the scoped DbContext from the request scope — never capture it in this lambda.
                var db = context.HttpContext.RequestServices.GetRequiredService<CourtlyDbContext>();
                var isRevoked = await db.RevokedTokens.AsNoTracking().AnyAsync(r => r.Jti == jti);
                if (isRevoked)
                {
                    context.Fail("Token has been revoked.");
                }
            },
            // F18: add OnMessageReceived here to read access_token from the query string for the SignalR hub.
        };
    });

builder.Services.AddAuthorization();

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

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Courtly API");
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
