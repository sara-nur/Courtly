using Courtly.Application.Abstractions;
using Courtly.Application.Recommendations;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the recommender (feature 29). <see cref="RecommendationService"/> is <c>Scoped</c> (it injects the
/// scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/> and the request-scoped
/// <see cref="ICurrentUser"/> / <see cref="ICourtService"/>). Called once from <c>Program.cs</c> alongside the other
/// feature registrations. The feedback validator is picked up by the existing FluentValidation assembly scan (no
/// explicit registration here).
/// </summary>
public static class RecommendationsServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyRecommendations(this IServiceCollection services)
    {
        services.AddScoped<IRecommendationService, RecommendationService>();

        return services;
    }
}
