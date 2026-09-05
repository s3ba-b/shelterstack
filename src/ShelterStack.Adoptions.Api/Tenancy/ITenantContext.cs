namespace ShelterStack.Adoptions.Api.Tenancy;

/// <summary>
/// Resolves the tenant the current unit of work is scoped to. Registered per-request (scoped)
/// so every EF Core query filter and downstream service sees a single, consistent tenant.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}
