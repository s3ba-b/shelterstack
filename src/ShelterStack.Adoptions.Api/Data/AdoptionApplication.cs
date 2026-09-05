namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// A tenant-scoped adoption application — an applicant's request to adopt one animal, recorded
/// by shelter staff on their behalf (there is no public adopter portal; see CHARTER.md's scope
/// boundaries). <see cref="TenantId"/> and the EF Core global query filter built on it (see
/// <see cref="AdoptionsDbContext"/>) carry the project's non-negotiable isolation rule.
/// </summary>
public sealed class AdoptionApplication
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// The animal applied for, referenced by id only. ShelterStack.Animals.Api owns the animal
    /// schema in its own database, so there is deliberately no foreign key here — the two
    /// services stay independently deployable and talk over the broker, not over a shared table.
    /// </summary>
    public Guid AnimalId { get; set; }

    public string ApplicantName { get; set; } = string.Empty;

    public string ApplicantEmail { get; set; } = string.Empty;

    /// <summary>Optional — many applicants leave only an email address.</summary>
    public string? ApplicantPhone { get; set; }

    /// <summary>Optional free-text address; a home check needs it, an early enquiry may not.</summary>
    public string? ApplicantAddress { get; set; }

    /// <summary>Free-text notes staff record alongside the application.</summary>
    public string? Notes { get; set; }

    public AdoptionApplicationStatus Status { get; set; } = AdoptionApplicationStatus.Submitted;

    /// <summary>
    /// Why the application is in its current status: the shelter's reason for a
    /// <see cref="AdoptionApplicationStatus.Rejected"/> decision, or the downstream reason
    /// carried by <c>AnimalStatusChangeRejected</c> for
    /// <see cref="AdoptionApplicationStatus.NeedsAttention"/>.
    /// </summary>
    public string? StatusReason { get; set; }

    public DateTimeOffset SubmittedAtUtc { get; set; }

    /// <summary>When the application was approved or rejected; null while it is still open.</summary>
    public DateTimeOffset? DecidedAtUtc { get; set; }
}
