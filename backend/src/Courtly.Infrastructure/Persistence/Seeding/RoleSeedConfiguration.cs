using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the Admin/Staff/User identity roles via HasData. Discovered by <c>ApplyConfigurationsFromAssembly</c>
/// (assembly scan, namespace-independent) and applied after Identity's own role mapping, so it only contributes
/// seed rows and leaves the schema to the base configuration.
/// </summary>
public class RoleSeedConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(SeedData.IdentityRoles);
    }
}
