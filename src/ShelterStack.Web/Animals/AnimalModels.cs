namespace ShelterStack.Web.Animals;

/// <summary>
/// The kind of animal, mirroring <c>ShelterStack.Animals.Api.Data.AnimalSpecies</c>. The Web app
/// keeps its own copy so it never takes a project reference on a business service; the names must
/// stay in step with the API's enum, since they are what crosses the wire (serialized by name).
/// </summary>
public enum AnimalSpecies
{
    Dog,
    Cat,
    Rabbit,
    Bird,
    Other,
}

/// <summary>An animal's sex, mirroring the Animals API enum. Serialized by name.</summary>
public enum AnimalSex
{
    Unknown,
    Male,
    Female,
}

/// <summary>Where an animal sits in its shelter lifecycle, mirroring the Animals API enum.
/// Read-only in M3's Animals screens — status transitions are the next increment.</summary>
public enum AnimalStatus
{
    Intake,
    Available,
    Adopted,
    Fostered,
    MedicalHold,
    Returned,
}

/// <summary>The animal resource as returned by the Gateway's <c>/animals</c> read endpoints.
/// Shape mirrors <c>ShelterStack.Animals.Api.AnimalResponse</c>.</summary>
public sealed record AnimalResponse(
    Guid Id,
    string Name,
    AnimalSpecies Species,
    string? Breed,
    AnimalSex Sex,
    DateOnly? DateOfBirth,
    string? Description,
    AnimalStatus Status
);

/// <summary>The body posted to the Gateway's create/update <c>/animals</c> endpoints. The tenant
/// is never sent — the backend resolves it from the Bearer token's claim.</summary>
public sealed record AnimalWriteRequest(
    string Name,
    AnimalSpecies Species,
    string? Breed,
    AnimalSex Sex,
    DateOnly? DateOfBirth,
    string? Description
);

/// <summary>
/// The editable form state behind the create and edit screens. Kept mutable (unlike the wire
/// records) so Blazor's two-way <c>@bind</c> can drive it; <see cref="ToRequest"/> and
/// <see cref="FromResponse"/> convert to and from the wire shape. Validation lives in the form
/// component (rather than data-annotation attributes) so its messages localize with the rest of
/// the UI.
/// </summary>
public sealed class AnimalFormModel
{
    public string Name { get; set; } = string.Empty;

    public AnimalSpecies Species { get; set; } = AnimalSpecies.Dog;

    public string? Breed { get; set; }

    public AnimalSex Sex { get; set; } = AnimalSex.Unknown;

    public DateOnly? DateOfBirth { get; set; }

    public string? Description { get; set; }

    public AnimalWriteRequest ToRequest() =>
        new(
            Name.Trim(),
            Species,
            string.IsNullOrWhiteSpace(Breed) ? null : Breed.Trim(),
            Sex,
            DateOfBirth,
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
        );

    public static AnimalFormModel FromResponse(AnimalResponse animal) =>
        new()
        {
            Name = animal.Name,
            Species = animal.Species,
            Breed = animal.Breed,
            Sex = animal.Sex,
            DateOfBirth = animal.DateOfBirth,
            Description = animal.Description,
        };
}
