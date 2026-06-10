namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the dynamic half of the demo data (users, courts, images, slots, reservations, payments, reviews,
/// notifications, news) after migrations have applied the HasData reference rows. Idempotent: every block is
/// guarded by an existence check, so calling it repeatedly (e.g. on every app start) never duplicates rows.
/// </summary>
public interface ICourtlyDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
