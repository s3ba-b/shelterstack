using Microsoft.EntityFrameworkCore;
using ShelterStack.Adoptions.Api.Tenancy;

namespace ShelterStack.Adoptions.Api.Data;

public sealed class AdoptionsDbContext(
    DbContextOptions<AdoptionsDbContext> options,
    ITenantContext tenantContext
) : DbContext(options)
{
    public DbSet<AdoptionApplication> AdoptionApplications => Set<AdoptionApplication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdoptionApplication>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ApplicantName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.ApplicantEmail).IsRequired().HasMaxLength(320);
            entity.Property(a => a.ApplicantPhone).HasMaxLength(50);
            entity.Property(a => a.ApplicantAddress).HasMaxLength(500);
            entity.Property(a => a.Notes).HasMaxLength(2000);
            entity.Property(a => a.StatusReason).HasMaxLength(500);

            // Enum stored as its string name (same pattern as Animals' AnimalStatus) so the
            // column stays readable and survives reordering of the enum members.
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            // Applications are almost always looked up per animal (how many people applied for
            // this dog?), so the AnimalId lookup gets an index — composed with TenantId because
            // the query filter below puts TenantId in front of every one of those queries.
            entity.HasIndex(a => new { a.TenantId, a.AnimalId });

            // Core tenant isolation mechanism: every query against AdoptionApplications is
            // implicitly scoped to the resolved tenant — including the queries the broker
            // consumers run, which resolve their tenant from the message rather than a request.
            // Inserts are unaffected, which is what lets startup seeding write rows for
            // multiple tenants through a single context instance.
            entity.HasQueryFilter(a => a.TenantId == tenantContext.TenantId);
        });
    }
}
