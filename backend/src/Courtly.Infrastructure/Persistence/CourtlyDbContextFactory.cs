using Courtly.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Courtly.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> can build the context without booting the API host
/// (whose top-level <c>Program.cs</c> runs option validation). Loads the nearest <c>.env</c> and reads
/// <c>DB_CONNECTION_STRING</c>, then applies the shared options. <c>migrations add</c> does not open a connection.
/// </summary>
public sealed class CourtlyDbContextFactory : IDesignTimeDbContextFactory<CourtlyDbContext>
{
    public CourtlyDbContext CreateDbContext(string[] args)
    {
        EnvironmentLoader.Load();

        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DB_CONNECTION_STRING is not set. Ensure backend/.env (or repo-root .env) defines it before running EF migrations.");
        }

        var options = new DbContextOptionsBuilder<CourtlyDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureCourtlyDbContext(options, connectionString);
        return new CourtlyDbContext(options.Options);
    }
}
