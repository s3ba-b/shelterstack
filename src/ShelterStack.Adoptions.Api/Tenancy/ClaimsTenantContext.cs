using ShelterStack.Adoptions.Api.Auth;

namespace ShelterStack.Adoptions.Api.Tenancy;

/// <summary>
/// Resolves the tenant from the authenticated caller's validated <c>tenant_id</c> claim.
/// Authentication runs before the endpoint, so by the time a tenant-scoped
/// <see cref="ITenantContext"/> is resolved the principal is present and the claim is
/// trustworthy — the tenant is never taken from a request header or body.
/// </summary>
public sealed class ClaimsTenantContext : ITenantContext
{
    public ClaimsTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user =
            httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "Tenant resolution requires an active HTTP request."
            );

        var tenantClaim = user.FindFirst(TokenAuth.TenantIdClaim)?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new InvalidOperationException(
                $"Authenticated token is missing a valid '{TokenAuth.TenantIdClaim}' claim (expected a GUID)."
            );
        }

        TenantId = tenantId;
    }

    public Guid TenantId { get; }
}
