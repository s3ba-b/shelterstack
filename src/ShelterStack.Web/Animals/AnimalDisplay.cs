using Microsoft.Extensions.Localization;

namespace ShelterStack.Web.Animals;

/// <summary>
/// Presentation helpers shared by the Animals screens: the emoji/thumb styling that stands in for
/// a photo (the design reference uses species emoji), the status → badge-colour mapping, and a
/// coarse age label. Localized text goes through the passed <see cref="IStringLocalizer{T}"/> so
/// both languages stay at parity; the emoji and CSS class names are language-neutral.
/// </summary>
public static class AnimalDisplay
{
    public static string Emoji(AnimalSpecies species) =>
        species switch
        {
            AnimalSpecies.Dog => "🐕",
            AnimalSpecies.Cat => "🐈",
            AnimalSpecies.Rabbit => "🐇",
            AnimalSpecies.Bird => "🐦",
            _ => "🐾",
        };

    public static string ThumbClass(AnimalSpecies species) =>
        species switch
        {
            AnimalSpecies.Dog => "t-dog",
            AnimalSpecies.Cat => "t-cat",
            AnimalSpecies.Rabbit => "t-rabbit",
            _ => "t-other",
        };

    /// <summary>The badge modifier class matching the status palette in <c>app.css</c>. Statuses
    /// the design reference didn't colour (Intake, Returned) fall back to a neutral pill.</summary>
    public static string BadgeClass(AnimalStatus status) =>
        status switch
        {
            AnimalStatus.Available => "available",
            AnimalStatus.Fostered => "fostered",
            AnimalStatus.Adopted => "adopted",
            AnimalStatus.MedicalHold => "medical",
            _ => "neutral",
        };

    public static string SpeciesLabel(IStringLocalizer localizer, AnimalSpecies species) =>
        localizer[$"Species_{species}"];

    public static string SexLabel(IStringLocalizer localizer, AnimalSex sex) =>
        localizer[$"Sex_{sex}"];

    public static string StatusLabel(IStringLocalizer localizer, AnimalStatus status) =>
        localizer[$"Status_{status}"];

    /// <summary>A coarse age label ("2 yr", "8 mo") derived from the date of birth, deliberately
    /// avoiding grammatical plurals so both languages read correctly with an abbreviated unit.</summary>
    public static string Age(IStringLocalizer localizer, DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth is not { } birth || birth > today)
        {
            return localizer["Age_Unknown"];
        }

        var months = ((today.Year - birth.Year) * 12) + today.Month - birth.Month;
        if (today.Day < birth.Day)
        {
            months--;
        }

        if (months < 1)
        {
            return localizer["Age_LessThanMonth"];
        }

        return months < 24 ? localizer["Age_Months", months] : localizer["Age_Years", months / 12];
    }
}
