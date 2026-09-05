using Microsoft.EntityFrameworkCore;
using ShelterStack.Adoptions.Api.Data;
using ShelterStack.Adoptions.Api.Tenancy;
using Xunit;

namespace ShelterStack.Adoptions.Api.Tests;

public class TenantQueryFilterTests
{
    private static AdoptionsDbContext CreateContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AdoptionsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AdoptionsDbContext(options, new StaticTenantContext(tenantId));
    }

    private static AdoptionApplication Application(Guid tenantId, string applicantName) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AnimalId = Guid.NewGuid(),
            ApplicantName = applicantName,
            ApplicantEmail = $"{applicantName.ToLowerInvariant()}@example.com",
            SubmittedAtUtc = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Query_OnlyReturnsRowsForTheResolvedTenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Filters apply to queries, not inserts: one context can seed rows for both tenants.
        await using (var seedContext = CreateContext(databaseName, tenantA))
        {
            seedContext.AdoptionApplications.AddRange(
                Application(tenantA, "Ada"),
                Application(tenantB, "Bruno")
            );
            await seedContext.SaveChangesAsync();
        }

        await using var tenantAContext = CreateContext(databaseName, tenantA);
        var tenantAApplications = await tenantAContext.AdoptionApplications.ToListAsync();
        Assert.Single(tenantAApplications);
        Assert.Equal("Ada", tenantAApplications[0].ApplicantName);

        await using var tenantBContext = CreateContext(databaseName, tenantB);
        var tenantBApplications = await tenantBContext.AdoptionApplications.ToListAsync();
        Assert.Single(tenantBApplications);
        Assert.Equal("Bruno", tenantBApplications[0].ApplicantName);
    }

    [Fact]
    public async Task IgnoreQueryFilters_BypassesTenantScoping()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var context = CreateContext(databaseName, tenantA);
        context.AdoptionApplications.AddRange(
            Application(tenantA, "Ada"),
            Application(tenantB, "Bruno")
        );
        await context.SaveChangesAsync();

        var all = await context.AdoptionApplications.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count);
    }
}
