using ShelterStack.Animals.Api.Data;
using ShelterStack.Animals.Api.Tenancy;
using Xunit;

namespace ShelterStack.Animals.Api.Tests;

/// <summary>
/// Guards the two properties of the demo dataset that are easy to break silently: CHARTER.md's
/// "≥ 20 animals per tenant" success measure, and the deterministic ids
/// ShelterStack.Adoptions.Api's own demo seed references. There is no cross-service foreign key
/// to catch a drifting id scheme, so it is asserted here.
/// </summary>
public class DemoSeedTests
{
    [Fact]
    public void Seeds_AtLeastTwentyAnimals_PerDemoTenant()
    {
        var animals = DemoAnimals.All().ToList();

        Assert.True(animals.Count(a => a.TenantId == DemoTenants.Northside) >= 20);
        Assert.True(animals.Count(a => a.TenantId == DemoTenants.Riverside) >= 20);
    }

    [Fact]
    public void Seeds_ASpreadOfStatuses_PerDemoTenant()
    {
        foreach (var tenantId in new[] { DemoTenants.Northside, DemoTenants.Riverside })
        {
            var statuses = DemoAnimals
                .All()
                .Where(a => a.TenantId == tenantId)
                .Select(a => a.Status)
                .ToHashSet();

            // The adoption flow needs all three to be demonstrable end to end: an animal that
            // can be adopted, one that can be adopted straight out of foster, and one whose
            // approval must be refused and land the application in NeedsAttention.
            Assert.Contains(AnimalStatus.Available, statuses);
            Assert.Contains(AnimalStatus.Fostered, statuses);
            Assert.Contains(AnimalStatus.MedicalHold, statuses);
        }
    }

    [Fact]
    public void AnimalIds_AreDeterministic_AndFollowThePublishedScheme()
    {
        var animals = DemoAnimals.All().ToList();

        var northside = animals.Where(a => a.TenantId == DemoTenants.Northside).ToList();
        var riverside = animals.Where(a => a.TenantId == DemoTenants.Riverside).ToList();

        // 1-based index, in seed order — the contract ShelterStack.Adoptions.Api's DemoAnimals
        // copy relies on.
        Assert.Equal(DemoAnimals.Northside(1), northside[0].Id);
        Assert.Equal("Buddy", northside[0].Name);
        Assert.Equal(DemoAnimals.Riverside(1), riverside[0].Id);
        Assert.Equal("Whiskers", riverside[0].Name);

        Assert.Equal(new Guid("11110001-0000-0000-0000-000000000000"), DemoAnimals.Northside(1));
        Assert.Equal(new Guid("22220020-0000-0000-0000-000000000000"), DemoAnimals.Riverside(20));

        // Regenerating must produce the same ids, and every id must be distinct.
        Assert.Equal(animals.Select(a => a.Id), DemoAnimals.All().Select(a => a.Id));
        Assert.Equal(animals.Count, animals.Select(a => a.Id).Distinct().Count());
    }
}
