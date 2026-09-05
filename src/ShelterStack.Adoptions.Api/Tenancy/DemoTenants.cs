namespace ShelterStack.Adoptions.Api.Tenancy;

/// <summary>
/// Fixed ids for the two demo tenants seeded at startup, matching the values used by
/// ShelterStack.Identity.Api and ShelterStack.Animals.Api so the seeded demo data lines up
/// across services and the cross-tenant isolation tests can target them directly.
/// </summary>
public static class DemoTenants
{
    public static readonly Guid Northside = new("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Riverside = new("22222222-2222-2222-2222-222222222222");
}
