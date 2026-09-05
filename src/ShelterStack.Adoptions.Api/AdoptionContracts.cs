using ShelterStack.Adoptions.Api.Data;

namespace ShelterStack.Adoptions.Api;

/// <summary>
/// Fields accepted when staff record an adoption application on an applicant's behalf.
/// <c>TenantId</c> is deliberately absent — it is taken from the caller's authenticated token,
/// never the request body — and so is <c>Status</c>, which always starts at
/// <see cref="AdoptionApplicationStatus.Submitted"/>.
/// </summary>
public sealed record CreateAdoptionApplicationRequest(
    Guid AnimalId,
    string ApplicantName,
    string ApplicantEmail,
    string? ApplicantPhone,
    string? ApplicantAddress,
    string? Notes
);

/// <summary>Why the shelter is turning an application down; surfaced back to staff on the
/// application itself.</summary>
public sealed record RejectAdoptionApplicationRequest(string? Reason);

/// <summary>The resource shape returned by the read and write endpoints.</summary>
public sealed record AdoptionApplicationResponse(
    Guid Id,
    Guid AnimalId,
    string ApplicantName,
    string ApplicantEmail,
    string? ApplicantPhone,
    string? ApplicantAddress,
    string? Notes,
    AdoptionApplicationStatus Status,
    string? StatusReason,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc
)
{
    public static AdoptionApplicationResponse From(AdoptionApplication application) =>
        new(
            application.Id,
            application.AnimalId,
            application.ApplicantName,
            application.ApplicantEmail,
            application.ApplicantPhone,
            application.ApplicantAddress,
            application.Notes,
            application.Status,
            application.StatusReason,
            application.SubmittedAtUtc,
            application.DecidedAtUtc
        );
}
