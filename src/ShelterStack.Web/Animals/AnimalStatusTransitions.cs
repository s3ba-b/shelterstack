namespace ShelterStack.Web.Animals;

/// <summary>
/// The allowed-transitions policy for <see cref="AnimalStatus"/>, mirroring the Animals API's
/// <c>ShelterStack.Animals.Api.Data.AnimalStatusTransitions</c>. The Web app keeps its own copy so
/// it never references a business service; the two must stay in step. It drives which target
/// statuses the "change status" control offers, so staff are only presented with legal moves — but
/// the API remains the source of truth, and the detail screen still surfaces a rejected move (e.g.
/// if the animal's status changed underneath) as a clear error rather than a crash.
/// </summary>
public static class AnimalStatusTransitions
{
    private static readonly Dictionary<AnimalStatus, AnimalStatus[]> Allowed = new()
    {
        [AnimalStatus.Intake] = [AnimalStatus.Available, AnimalStatus.MedicalHold],
        [AnimalStatus.Available] =
        [
            AnimalStatus.Adopted,
            AnimalStatus.Fostered,
            AnimalStatus.MedicalHold,
        ],
        [AnimalStatus.Fostered] =
        [
            AnimalStatus.Available,
            AnimalStatus.Adopted,
            AnimalStatus.MedicalHold,
        ],
        [AnimalStatus.MedicalHold] = [AnimalStatus.Available, AnimalStatus.Intake],
        [AnimalStatus.Adopted] = [AnimalStatus.Returned],
        [AnimalStatus.Returned] = [AnimalStatus.Intake, AnimalStatus.Available],
    };

    /// <summary>The statuses an animal may legally move to from its current one, in declaration
    /// order. Empty when the status is terminal for this policy.</summary>
    public static IReadOnlyList<AnimalStatus> TargetsFrom(AnimalStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
