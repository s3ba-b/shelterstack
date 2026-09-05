using ShelterStack.Adoptions.Api.Data;
using ShelterStack.Adoptions.Api.Tenancy;
using Xunit;

namespace ShelterStack.Adoptions.Api.Tests;

/// <summary>
/// Guards the two properties of the demo dataset that are easy to break silently: CHARTER.md's
/// "≥ 10 adoption applications per tenant" success measure, and the fact that every seeded
/// application points at an animal ShelterStack.Animals.Api actually seeds. There is no
/// cross-service foreign key to catch a drifting id, so the id scheme is asserted here.
/// </summary>
public class DemoSeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seeds_AtLeastTenApplications_PerDemoTenant()
    {
        var applications = DemoAdoptionApplications.All(Now).ToList();

        Assert.True(applications.Count(a => a.TenantId == DemoTenants.Northside) >= 10);
        Assert.True(applications.Count(a => a.TenantId == DemoTenants.Riverside) >= 10);
    }

    [Fact]
    public void Seeds_ASpreadOfStatuses_IncludingNeedsAttention()
    {
        foreach (var tenantId in new[] { DemoTenants.Northside, DemoTenants.Riverside })
        {
            var statuses = DemoAdoptionApplications
                .All(Now)
                .Where(a => a.TenantId == tenantId)
                .Select(a => a.Status)
                .ToHashSet();

            Assert.Contains(AdoptionApplicationStatus.Submitted, statuses);
            Assert.Contains(AdoptionApplicationStatus.UnderReview, statuses);
            Assert.Contains(AdoptionApplicationStatus.Approved, statuses);
            Assert.Contains(AdoptionApplicationStatus.Rejected, statuses);
            Assert.Contains(AdoptionApplicationStatus.Withdrawn, statuses);
            Assert.Contains(AdoptionApplicationStatus.NeedsAttention, statuses);
        }
    }

    [Fact]
    public void Every_SeededApplication_ReferencesASeededAnimalOfItsOwnTenant()
    {
        // The 20 ids ShelterStack.Animals.Api seeds per demo tenant, computed the same way.
        var northsideAnimals = Enumerable.Range(1, 20).Select(DemoAnimals.Northside).ToHashSet();
        var riversideAnimals = Enumerable.Range(1, 20).Select(DemoAnimals.Riverside).ToHashSet();

        foreach (var application in DemoAdoptionApplications.All(Now))
        {
            var expected =
                application.TenantId == DemoTenants.Northside ? northsideAnimals : riversideAnimals;

            Assert.Contains(application.AnimalId, expected);
        }
    }

    [Fact]
    public void DecidedApplications_CarryADecisionTimestamp_AndOpenOnesDoNot()
    {
        foreach (var application in DemoAdoptionApplications.All(Now))
        {
            if (
                application.Status
                is AdoptionApplicationStatus.Approved
                    or AdoptionApplicationStatus.Rejected
            )
            {
                Assert.NotNull(application.DecidedAtUtc);
                Assert.True(application.DecidedAtUtc >= application.SubmittedAtUtc);
            }

            if (
                application.Status
                is AdoptionApplicationStatus.Submitted
                    or AdoptionApplicationStatus.UnderReview
            )
            {
                Assert.Null(application.DecidedAtUtc);
            }
        }
    }
}
