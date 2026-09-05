namespace ShelterStack.Adoptions.Api.Auth;

/// <summary>
/// Names of the custom claims carried by the access tokens issued by ShelterStack.Identity.Api
/// (mirroring its <c>JwtTokenIssuer</c>), the role values they hold, and the authorization
/// policies built on them. Deliberately a per-service copy of the Animals API's equivalent —
/// each service is self-contained rather than sharing a contracts assembly.
/// </summary>
public static class TokenAuth
{
    public const string TenantIdClaim = "tenant_id";
    public const string RoleClaim = "role";

    public const string AdminRole = "Admin";
    public const string StaffRole = "Staff";

    /// <summary>
    /// Adoption applications hold applicant personal data, so every endpoint in this service is
    /// restricted to shelter admins and staff; volunteers get a 403.
    /// </summary>
    public const string StaffOrAdminPolicy = "StaffOrAdmin";
}
