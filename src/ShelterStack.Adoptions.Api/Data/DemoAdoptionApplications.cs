using ShelterStack.Adoptions.Api.Tenancy;

namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// The representative demo dataset seeded at startup for the two demo tenants — CHARTER.md's
/// success measure asks for at least 10 adoption applications per tenant. The spread is chosen
/// to make the whole workflow visible at a glance: applications still open, ones decided either
/// way, one an applicant withdrew, and one that landed in
/// <see cref="AdoptionApplicationStatus.NeedsAttention"/> because the animal turned out to be on
/// a medical hold — the state the asynchronous approval's compensating path produces.
/// <para>
/// Every row points at a real animal via <see cref="DemoAnimals"/>, and the seeded statuses are
/// kept consistent with the animals' own seeded statuses (an approved application belongs to an
/// animal seeded as adopted, and the needs-attention one to an animal on a medical hold).
/// </para>
/// </summary>
public static class DemoAdoptionApplications
{
    /// <summary>
    /// Fresh <see cref="AdoptionApplication"/> instances for both demo tenants, dated relative
    /// to <paramref name="now"/> so the demo timeline always reads as recent.
    /// </summary>
    public static IEnumerable<AdoptionApplication> All(DateTimeOffset now) =>
        Build(DemoTenants.Northside, DemoAnimals.Northside, NorthsideRows, now)
            .Concat(Build(DemoTenants.Riverside, DemoAnimals.Riverside, RiversideRows, now));

    private static IEnumerable<AdoptionApplication> Build(
        Guid tenantId,
        Func<int, Guid> animalIdFor,
        IReadOnlyList<Row> rows,
        DateTimeOffset now
    ) =>
        rows.Select(row => new AdoptionApplication
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AnimalId = animalIdFor(row.AnimalIndex),
            ApplicantName = row.ApplicantName,
            ApplicantEmail = row.ApplicantEmail,
            ApplicantPhone = row.ApplicantPhone,
            ApplicantAddress = row.ApplicantAddress,
            Notes = row.Notes,
            Status = row.Status,
            StatusReason = row.StatusReason,
            SubmittedAtUtc = now.AddDays(-row.SubmittedDaysAgo),
            DecidedAtUtc = row.DecidedDaysAgo is { } decided ? now.AddDays(-decided) : null,
        });

    private sealed record Row(
        int AnimalIndex,
        string ApplicantName,
        string ApplicantEmail,
        string? ApplicantPhone,
        string? ApplicantAddress,
        string? Notes,
        AdoptionApplicationStatus Status,
        string? StatusReason,
        int SubmittedDaysAgo,
        int? DecidedDaysAgo
    );

    // Northside Shelter. Animal indexes follow that tenant's seeded animals: 1 Buddy, 2 Luna,
    // 3 Milo, 4 Nala, 5 Rocky, 6 Poppy, 9 Bruno, 11 Coco (fostered), 14 Ruby (medical hold),
    // 19 Bailey and 20 Smokey (already adopted).
    private static readonly Row[] NorthsideRows =
    [
        new(
            19,
            "Anna Kowalska",
            "anna.kowalska@example.com",
            "+48 601 234 567",
            "ul. Ogrodowa 14/3, 00-873 Warszawa",
            "Home check passed; fenced garden.",
            AdoptionApplicationStatus.Approved,
            null,
            SubmittedDaysAgo: 41,
            DecidedDaysAgo: 33
        ),
        new(
            20,
            "Marek Wiśniewski",
            "m.wisniewski@example.com",
            "+48 602 118 940",
            "ul. Lipowa 2, 05-500 Piaseczno",
            "Adopted from us before; references on file.",
            AdoptionApplicationStatus.Approved,
            null,
            SubmittedDaysAgo: 30,
            DecidedDaysAgo: 24
        ),
        new(
            1,
            "Julia Nowak",
            "julia.nowak@example.com",
            "+48 605 771 220",
            "ul. Krótka 8, 01-234 Warszawa",
            "First-time adopter; works from home.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 6,
            DecidedDaysAgo: null
        ),
        new(
            1,
            "Tomasz Lewandowski",
            "t.lewandowski@example.com",
            "+48 604 903 115",
            "ul. Polna 41, 05-092 Łomianki",
            "Second enquiry for the same dog; family with two teenagers.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 4,
            DecidedDaysAgo: null
        ),
        new(
            2,
            "Katarzyna Zielińska",
            "k.zielinska@example.com",
            "+48 692 440 108",
            "ul. Wiejska 19, 05-800 Pruszków",
            "Runs daily; home check booked.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 11,
            DecidedDaysAgo: null
        ),
        new(
            3,
            "Paweł Dąbrowski",
            "p.dabrowski@example.com",
            null,
            null,
            "Has one resident cat; asked about a slow introduction.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 9,
            DecidedDaysAgo: null
        ),
        new(
            5,
            "Ewa Szymańska",
            "ewa.szymanska@example.com",
            "+48 668 301 774",
            "ul. Sadowa 6, 04-505 Warszawa",
            "No other animals at home — a good fit for an only dog.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 14,
            DecidedDaysAgo: null
        ),
        new(
            6,
            "Michał Kaczmarek",
            "m.kaczmarek@example.com",
            "+48 660 552 019",
            "ul. Miodowa 3, 02-100 Warszawa",
            "Told that Poppy and Clover must be homed together.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 3,
            DecidedDaysAgo: null
        ),
        new(
            9,
            "Agnieszka Wójcik",
            "a.wojcik@example.com",
            "+48 663 118 402",
            "ul. Słoneczna 22, 00-710 Warszawa",
            null,
            AdoptionApplicationStatus.Rejected,
            "A third-floor flat with no lift is not workable for a senior dog with hip problems.",
            SubmittedDaysAgo: 25,
            DecidedDaysAgo: 19
        ),
        new(
            11,
            "Bartosz Mazur",
            "b.mazur@example.com",
            "+48 691 774 300",
            "ul. Cicha 11, 05-270 Marki",
            "Her current foster carer — foster-to-adopt.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 8,
            DecidedDaysAgo: null
        ),
        new(
            4,
            "Zofia Krawczyk",
            "z.krawczyk@example.com",
            null,
            "ul. Długa 57, 03-301 Warszawa",
            "Withdrew after a change of circumstances.",
            AdoptionApplicationStatus.Withdrawn,
            null,
            SubmittedDaysAgo: 20,
            DecidedDaysAgo: null
        ),
        new(
            14,
            "Rafał Piotrowski",
            "r.piotrowski@example.com",
            "+48 605 220 916",
            "ul. Leśna 4, 05-420 Józefów",
            "Approved before the dental surgery was booked.",
            AdoptionApplicationStatus.NeedsAttention,
            "Cannot move an animal from MedicalHold to Adopted.",
            SubmittedDaysAgo: 12,
            DecidedDaysAgo: 5
        ),
    ];

    // Riverside Rescue. Animal indexes follow that tenant's seeded animals: 1 Whiskers,
    // 2 Bella, 3 Charlie, 4 Tigger, 5 Daisy, 6 Hazel, 8 Suki, 9 Rufus, 12 Boomer (fostered),
    // 15 Freya (medical hold), 19 Duke and 20 Cleo (already adopted).
    private static readonly Row[] RiversideRows =
    [
        new(
            19,
            "Hanna Woźniak",
            "h.wozniak@example.com",
            "+48 601 909 118",
            "ul. Nadrzeczna 8, 30-147 Kraków",
            "Smallholding with two hectares; ideal for a large dog.",
            AdoptionApplicationStatus.Approved,
            null,
            SubmittedDaysAgo: 47,
            DecidedDaysAgo: 38
        ),
        new(
            20,
            "Piotr Grabowski",
            "p.grabowski@example.com",
            "+48 604 330 771",
            "ul. Zamkowa 15/2, 31-014 Kraków",
            "Volunteers here on weekends; knows the cat well.",
            AdoptionApplicationStatus.Approved,
            null,
            SubmittedDaysAgo: 28,
            DecidedDaysAgo: 21
        ),
        new(
            1,
            "Alicja Pawlak",
            "a.pawlak@example.com",
            "+48 690 441 227",
            "ul. Spokojna 9, 30-390 Kraków",
            "Quiet single-person household — a good match for a shy cat.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 5,
            DecidedDaysAgo: null
        ),
        new(
            2,
            "Jakub Michalski",
            "j.michalski@example.com",
            "+48 667 200 543",
            "ul. Kwiatowa 33, 32-050 Skawina",
            "Home check booked for next week.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 13,
            DecidedDaysAgo: null
        ),
        new(
            3,
            "Natalia Adamczyk",
            "n.adamczyk@example.com",
            null,
            null,
            "Asked about lead training support.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 7,
            DecidedDaysAgo: null
        ),
        new(
            4,
            "Krzysztof Sikora",
            "k.sikora@example.com",
            "+48 606 813 990",
            "ul. Wesoła 12, 30-011 Kraków",
            "Experienced with high-energy cats.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 2,
            DecidedDaysAgo: null
        ),
        new(
            5,
            "Magdalena Baran",
            "m.baran@example.com",
            "+48 698 117 654",
            "ul. Podgórska 4, 30-540 Kraków",
            "Second visit went well; references being checked.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 16,
            DecidedDaysAgo: null
        ),
        new(
            6,
            "Łukasz Duda",
            "l.duda@example.com",
            "+48 602 774 001",
            "ul. Ogrodnicza 27, 32-020 Wieliczka",
            "Building an outdoor run before collection.",
            AdoptionApplicationStatus.Submitted,
            null,
            SubmittedDaysAgo: 10,
            DecidedDaysAgo: null
        ),
        new(
            8,
            "Weronika Jaworska",
            "w.jaworska@example.com",
            null,
            "ul. Rynek 2, 33-100 Tarnów",
            null,
            AdoptionApplicationStatus.Rejected,
            "Household is away for most of the week; this cat needs daily company.",
            SubmittedDaysAgo: 22,
            DecidedDaysAgo: 17
        ),
        new(
            12,
            "Adrian Sadowski",
            "a.sadowski@example.com",
            "+48 665 019 338",
            "ul. Górna 18, 32-400 Myślenice",
            "His foster carer, an experienced handler — foster-to-adopt.",
            AdoptionApplicationStatus.UnderReview,
            null,
            SubmittedDaysAgo: 9,
            DecidedDaysAgo: null
        ),
        new(
            9,
            "Karolina Bąk",
            "k.bak@example.com",
            "+48 693 550 214",
            "ul. Jasna 6, 30-218 Kraków",
            "Withdrew — moving abroad for work.",
            AdoptionApplicationStatus.Withdrawn,
            null,
            SubmittedDaysAgo: 26,
            DecidedDaysAgo: null
        ),
        new(
            15,
            "Sebastian Ostrowski",
            "s.ostrowski@example.com",
            "+48 604 118 772",
            "ul. Krakowska 88, 32-080 Zabierzów",
            "Approved before the orthopaedic assessment was scheduled.",
            AdoptionApplicationStatus.NeedsAttention,
            "Cannot move an animal from MedicalHold to Adopted.",
            SubmittedDaysAgo: 15,
            DecidedDaysAgo: 6
        ),
    ];
}
