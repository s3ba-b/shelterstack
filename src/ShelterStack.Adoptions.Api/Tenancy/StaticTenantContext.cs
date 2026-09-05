namespace ShelterStack.Adoptions.Api.Tenancy;

/// <summary>
/// Fixed-tenant context for code paths that run outside an HTTP request
/// (startup seeding, design-time migrations, broker message handling, tests) where
/// <see cref="ClaimsTenantContext"/> has no authenticated principal to resolve from.
/// </summary>
public sealed class StaticTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
}
