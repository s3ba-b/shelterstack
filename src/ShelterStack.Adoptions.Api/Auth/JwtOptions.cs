namespace ShelterStack.Adoptions.Api.Auth;

/// <summary>
/// Token-validation settings for the access tokens issued by ShelterStack.Identity.Api, bound
/// from the "Jwt" configuration section. The signing key, issuer, and audience must match the
/// Identity service's values so its tokens validate here; the key in appsettings.json is a
/// development/demo key and must be overridden via a secret in any real deployment.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}
