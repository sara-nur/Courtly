using Courtly.Application.Reviews;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for court reviews (feature 24). <see cref="ReviewService"/> is <c>Scoped</c> (it injects the scoped
/// <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/> and the request-scoped
/// <see cref="Courtly.Application.Abstractions.ICurrentUser"/>). Validators need no per-validator registration — the
/// <c>Program.cs</c> assembly scan picks them up. Called once from <c>Program.cs</c> alongside the other feature
/// registrations.
/// </summary>
public static class ReviewServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyReviews(this IServiceCollection services)
    {
        services.AddScoped<IReviewService, ReviewService>();

        return services;
    }
}
