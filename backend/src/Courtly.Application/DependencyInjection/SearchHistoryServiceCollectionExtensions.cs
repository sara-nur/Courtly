using Courtly.Application.SearchHistory;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the search-history capture subsystem (feature 23 recommender signal). <see cref="SearchHistoryService"/>
/// is <c>Scoped</c> (it injects the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/> and the
/// request-scoped <see cref="Courtly.Application.Abstractions.ICurrentUser"/>). Called once from <c>Program.cs</c>
/// alongside the other feature registrations.
/// </summary>
public static class SearchHistoryServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlySearchHistory(this IServiceCollection services)
    {
        services.AddScoped<ISearchHistoryService, SearchHistoryService>();

        return services;
    }
}
