namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// Where an adoption application sits in the review process. A newly recorded application
/// starts at <see cref="Submitted"/>. Persisted as its string name (see
/// <see cref="AdoptionsDbContext"/>) so the column stays readable and survives reordering.
/// </summary>
public enum AdoptionApplicationStatus
{
    /// <summary>Recorded by staff on the applicant's behalf; awaiting review.</summary>
    Submitted,

    /// <summary>Staff have picked the application up — home check, references, etc.</summary>
    UnderReview,

    /// <summary>Approved. The animal's move to <c>Adopted</c> is applied asynchronously by
    /// ShelterStack.Animals.Api in response to the <c>AdoptionApproved</c> event.</summary>
    Approved,

    /// <summary>Turned down by the shelter; the reason is kept in
    /// <see cref="AdoptionApplication.StatusReason"/>.</summary>
    Rejected,

    /// <summary>Pulled by the applicant before a decision was made.</summary>
    Withdrawn,

    /// <summary>
    /// The approval could not be completed downstream — ShelterStack.Animals.Api refused the
    /// animal's move to <c>Adopted</c> (e.g. it is on a medical hold) and published
    /// <c>AnimalStatusChangeRejected</c>. Because the approval is asynchronous the endpoint has
    /// long since returned 200, so this status is how the failure is surfaced to staff instead
    /// of being silently lost; the reason is kept in
    /// <see cref="AdoptionApplication.StatusReason"/>.
    /// </summary>
    NeedsAttention,
}
