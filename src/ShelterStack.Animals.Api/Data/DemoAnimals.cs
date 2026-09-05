using ShelterStack.Animals.Api.Tenancy;

namespace ShelterStack.Animals.Api.Data;

/// <summary>
/// The representative demo dataset seeded at startup for the two demo tenants — CHARTER.md's
/// success measure asks for at least 20 animals per tenant, spanning the statuses a real
/// shelter has on its books at any moment.
/// <para>
/// The ids are <b>deterministic</b>, derived from the tenant and a 1-based index, rather than
/// freshly generated: ShelterStack.Adoptions.Api seeds its demo adoption applications against
/// these same ids from its own database, and with no cross-service foreign key to lean on the
/// only way those references can line up is for both sides to compute the id the same way.
/// That service keeps its own copy of <see cref="Northside"/>/<see cref="Riverside"/>; the
/// duplication is deliberate, matching how each service keeps its own <c>ITenantContext</c>,
/// <c>TokenAuth</c>, and <c>DemoTenants</c> rather than sharing an assembly.
/// </para>
/// </summary>
public static class DemoAnimals
{
    /// <summary>How many animals each demo tenant is seeded with.</summary>
    public const int PerTenant = 20;

    /// <summary>Id of the <paramref name="index"/>-th (1-based) seeded Northside animal.</summary>
    public static Guid Northside(int index) => Id("1111", index);

    /// <summary>Id of the <paramref name="index"/>-th (1-based) seeded Riverside animal.</summary>
    public static Guid Riverside(int index) => Id("2222", index);

    private static Guid Id(string tenantPrefix, int index) =>
        new($"{tenantPrefix}{index:D4}-0000-0000-0000-000000000000");

    /// <summary>
    /// Fresh <see cref="Animal"/> instances for both demo tenants. Built on each call rather
    /// than cached, so the seeding context never tracks an entity another caller also holds.
    /// </summary>
    public static IEnumerable<Animal> All() =>
        Build(DemoTenants.Northside, Northside, NorthsideRows)
            .Concat(Build(DemoTenants.Riverside, Riverside, RiversideRows));

    private static IEnumerable<Animal> Build(
        Guid tenantId,
        Func<int, Guid> idFor,
        IReadOnlyList<Row> rows
    ) =>
        rows.Select(
            (row, offset) =>
                new Animal
                {
                    Id = idFor(offset + 1),
                    TenantId = tenantId,
                    Name = row.Name,
                    Species = row.Species,
                    Breed = row.Breed,
                    Sex = row.Sex,
                    DateOfBirth = row.DateOfBirth,
                    Description = row.Description,
                    Status = row.Status,
                }
        );

    private sealed record Row(
        string Name,
        AnimalSpecies Species,
        string? Breed,
        AnimalSex Sex,
        DateOnly DateOfBirth,
        string Description,
        AnimalStatus Status
    );

    // Northside Shelter. Index 1 stays "Buddy" — the animal the original M0 seed created and
    // the isolation suite still names.
    private static readonly Row[] NorthsideRows =
    [
        new(
            "Buddy",
            AnimalSpecies.Dog,
            "Labrador Retriever",
            AnimalSex.Male,
            new DateOnly(2021, 4, 12),
            "Friendly, house-trained; good with children.",
            AnimalStatus.Available
        ),
        new(
            "Luna",
            AnimalSpecies.Dog,
            "Border Collie mix",
            AnimalSex.Female,
            new DateOnly(2020, 8, 3),
            "High energy; needs an active home.",
            AnimalStatus.Available
        ),
        new(
            "Milo",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Male,
            new DateOnly(2023, 2, 18),
            "Playful; happiest with another cat.",
            AnimalStatus.Available
        ),
        new(
            "Nala",
            AnimalSpecies.Cat,
            "Maine Coon mix",
            AnimalSex.Female,
            new DateOnly(2019, 11, 25),
            "Calm lap cat; dislikes stairs.",
            AnimalStatus.Available
        ),
        new(
            "Rocky",
            AnimalSpecies.Dog,
            "Staffordshire Bull Terrier",
            AnimalSex.Male,
            new DateOnly(2018, 6, 30),
            "Gentle with people; best as an only dog.",
            AnimalStatus.Available
        ),
        new(
            "Poppy",
            AnimalSpecies.Rabbit,
            "Dutch",
            AnimalSex.Female,
            new DateOnly(2024, 3, 9),
            "Litter-trained; bonded with Clover.",
            AnimalStatus.Available
        ),
        new(
            "Clover",
            AnimalSpecies.Rabbit,
            "Dutch",
            AnimalSex.Female,
            new DateOnly(2024, 3, 9),
            "Bonded with Poppy — to be homed together.",
            AnimalStatus.Available
        ),
        new(
            "Kiwi",
            AnimalSpecies.Bird,
            "Budgerigar",
            AnimalSex.Unknown,
            new DateOnly(2023, 7, 14),
            "Hand-tame; whistles constantly.",
            AnimalStatus.Available
        ),
        new(
            "Bruno",
            AnimalSpecies.Dog,
            "German Shepherd",
            AnimalSex.Male,
            new DateOnly(2017, 1, 22),
            "Senior; knows his basic commands.",
            AnimalStatus.Available
        ),
        new(
            "Sasha",
            AnimalSpecies.Cat,
            "Russian Blue mix",
            AnimalSex.Female,
            new DateOnly(2022, 5, 5),
            "Returned after a placement broke down; being re-assessed.",
            AnimalStatus.Returned
        ),
        new(
            "Coco",
            AnimalSpecies.Dog,
            "Dachshund",
            AnimalSex.Female,
            new DateOnly(2021, 9, 17),
            "In a foster home while she rebuilds her confidence.",
            AnimalStatus.Fostered
        ),
        new(
            "Pepper",
            AnimalSpecies.Cat,
            "Domestic Longhair",
            AnimalSex.Female,
            new DateOnly(2024, 1, 8),
            "Fostered with her litter until they are weaned.",
            AnimalStatus.Fostered
        ),
        new(
            "Ziggy",
            AnimalSpecies.Dog,
            "Jack Russell mix",
            AnimalSex.Male,
            new DateOnly(2022, 12, 2),
            "Fostered with an experienced handler.",
            AnimalStatus.Fostered
        ),
        new(
            "Ruby",
            AnimalSpecies.Dog,
            "Beagle",
            AnimalSex.Female,
            new DateOnly(2020, 2, 11),
            "On a medical hold — dental surgery scheduled.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Oscar",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Male,
            new DateOnly(2016, 4, 27),
            "On a medical hold — kidney values being monitored.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Toffee",
            AnimalSpecies.Rabbit,
            "Lionhead",
            AnimalSex.Male,
            new DateOnly(2023, 10, 30),
            "On a medical hold — recovering from neutering.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Scout",
            AnimalSpecies.Dog,
            "Husky mix",
            AnimalSex.Male,
            new DateOnly(2023, 5, 19),
            "Just arrived; behavioural assessment pending.",
            AnimalStatus.Intake
        ),
        new(
            "Willow",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Female,
            new DateOnly(2024, 6, 21),
            "Came in as a stray; assessment pending.",
            AnimalStatus.Intake
        ),
        new(
            "Bailey",
            AnimalSpecies.Dog,
            "Golden Retriever",
            AnimalSex.Female,
            new DateOnly(2019, 3, 4),
            "Adopted by a family with two older children.",
            AnimalStatus.Adopted
        ),
        new(
            "Smokey",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Male,
            new DateOnly(2021, 7, 8),
            "Adopted by a repeat adopter.",
            AnimalStatus.Adopted
        ),
    ];

    // Riverside Rescue. Index 1 stays "Whiskers", for the same reason Northside's stays "Buddy".
    private static readonly Row[] RiversideRows =
    [
        new(
            "Whiskers",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Female,
            new DateOnly(2022, 9, 1),
            "Shy at first; prefers a quiet home.",
            AnimalStatus.Available
        ),
        new(
            "Bella",
            AnimalSpecies.Dog,
            "Cocker Spaniel",
            AnimalSex.Female,
            new DateOnly(2021, 11, 16),
            "Sociable; walks well on a lead.",
            AnimalStatus.Available
        ),
        new(
            "Charlie",
            AnimalSpecies.Dog,
            "Labrador mix",
            AnimalSex.Male,
            new DateOnly(2019, 5, 28),
            "Loves water; strong on the lead.",
            AnimalStatus.Available
        ),
        new(
            "Tigger",
            AnimalSpecies.Cat,
            "Bengal mix",
            AnimalSex.Male,
            new DateOnly(2023, 1, 13),
            "Very active; needs plenty of enrichment.",
            AnimalStatus.Available
        ),
        new(
            "Daisy",
            AnimalSpecies.Dog,
            "Whippet",
            AnimalSex.Female,
            new DateOnly(2020, 10, 7),
            "Quiet indoors; sprints outdoors.",
            AnimalStatus.Available
        ),
        new(
            "Hazel",
            AnimalSpecies.Rabbit,
            "Mini Lop",
            AnimalSex.Female,
            new DateOnly(2024, 2, 20),
            "Used to children; needs a large run.",
            AnimalStatus.Available
        ),
        new(
            "Pip",
            AnimalSpecies.Bird,
            "Cockatiel",
            AnimalSex.Male,
            new DateOnly(2022, 4, 2),
            "Steps up readily; noisy at dawn.",
            AnimalStatus.Available
        ),
        new(
            "Suki",
            AnimalSpecies.Cat,
            "Siamese mix",
            AnimalSex.Female,
            new DateOnly(2018, 8, 24),
            "Vocal; bonds strongly with one person.",
            AnimalStatus.Available
        ),
        new(
            "Rufus",
            AnimalSpecies.Dog,
            "Boxer",
            AnimalSex.Male,
            new DateOnly(2022, 6, 11),
            "Boisterous; suits a household without small children.",
            AnimalStatus.Available
        ),
        new(
            "Marley",
            AnimalSpecies.Dog,
            "Greyhound",
            AnimalSex.Male,
            new DateOnly(2017, 12, 5),
            "Returned when his adopter moved abroad.",
            AnimalStatus.Returned
        ),
        new(
            "Juniper",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Female,
            new DateOnly(2023, 11, 9),
            "Fostered while she recovers from a road injury.",
            AnimalStatus.Fostered
        ),
        new(
            "Boomer",
            AnimalSpecies.Dog,
            "Rottweiler mix",
            AnimalSex.Male,
            new DateOnly(2021, 3, 26),
            "Fostered with an experienced handler.",
            AnimalStatus.Fostered
        ),
        new(
            "Nutmeg",
            AnimalSpecies.Rabbit,
            "Rex",
            AnimalSex.Female,
            new DateOnly(2024, 5, 15),
            "Fostered until she is old enough to be homed.",
            AnimalStatus.Fostered
        ),
        new(
            "Ash",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Male,
            new DateOnly(2020, 7, 19),
            "On a medical hold — respiratory infection.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Freya",
            AnimalSpecies.Dog,
            "Collie mix",
            AnimalSex.Female,
            new DateOnly(2018, 9, 12),
            "On a medical hold — orthopaedic assessment.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Mango",
            AnimalSpecies.Bird,
            "Budgerigar",
            AnimalSex.Female,
            new DateOnly(2023, 8, 30),
            "On a medical hold — treatment for feather plucking.",
            AnimalStatus.MedicalHold
        ),
        new(
            "Rosie",
            AnimalSpecies.Dog,
            "Terrier mix",
            AnimalSex.Female,
            new DateOnly(2024, 4, 4),
            "Just arrived; assessment pending.",
            AnimalStatus.Intake
        ),
        new(
            "Salem",
            AnimalSpecies.Cat,
            "Domestic Longhair",
            AnimalSex.Male,
            new DateOnly(2022, 2, 14),
            "Came in from a hoarding case; assessment pending.",
            AnimalStatus.Intake
        ),
        new(
            "Duke",
            AnimalSpecies.Dog,
            "Great Dane mix",
            AnimalSex.Male,
            new DateOnly(2019, 1, 30),
            "Adopted by a rural family with land.",
            AnimalStatus.Adopted
        ),
        new(
            "Cleo",
            AnimalSpecies.Cat,
            "Domestic Shorthair",
            AnimalSex.Female,
            new DateOnly(2021, 6, 23),
            "Adopted by a long-standing volunteer.",
            AnimalStatus.Adopted
        ),
    ];
}
