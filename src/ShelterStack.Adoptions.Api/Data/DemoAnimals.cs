namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// The deterministic ids ShelterStack.Animals.Api gives the animals it seeds for the two demo
/// tenants, so this service's demo applications can reference real animals.
/// <para>
/// A per-service copy of that service's <c>DemoAnimals</c> id scheme, matching how every
/// service already keeps its own <c>ITenantContext</c>, <c>TokenAuth</c>, and
/// <c>DemoTenants</c>. There is no cross-service foreign key to lean on — each service owns its
/// own database — so agreeing on how the id is computed is what makes the reference line up.
/// If the scheme changes there, it changes here.
/// </para>
/// </summary>
public static class DemoAnimals
{
    /// <summary>Id of the <paramref name="index"/>-th (1-based) seeded Northside animal.</summary>
    public static Guid Northside(int index) => Id("1111", index);

    /// <summary>Id of the <paramref name="index"/>-th (1-based) seeded Riverside animal.</summary>
    public static Guid Riverside(int index) => Id("2222", index);

    private static Guid Id(string tenantPrefix, int index) =>
        new($"{tenantPrefix}{index:D4}-0000-0000-0000-000000000000");
}
