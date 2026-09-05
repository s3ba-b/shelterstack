namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// The small amount of policy around <see cref="AdoptionApplicationStatus"/> that more than one
/// caller needs. The richer allowed-transitions table lives on the animal side
/// (<c>AnimalStatusTransitions</c>); an application only ever moves out of an open state once.
/// </summary>
public static class AdoptionApplicationStatusRules
{
    /// <summary>
    /// Whether an application is still open, i.e. can be approved or rejected. An already
    /// decided, withdrawn, or downstream-failed application is not decided a second time.
    /// </summary>
    public static bool IsDecidable(AdoptionApplicationStatus status) =>
        status is AdoptionApplicationStatus.Submitted or AdoptionApplicationStatus.UnderReview;
}
