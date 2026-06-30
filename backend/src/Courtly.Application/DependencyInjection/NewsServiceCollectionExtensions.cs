using Courtly.Application.Abstractions;
using Courtly.Application.News;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the news / announcements subsystem (feature 21). <see cref="NewsService"/> is <c>Scoped</c> (it
/// injects the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). Called once from
/// <c>Program.cs</c> alongside the other feature registrations.
/// </summary>
public static class NewsServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyNews(this IServiceCollection services)
    {
        services.AddScoped<INewsService, NewsService>();

        return services;
    }
}
