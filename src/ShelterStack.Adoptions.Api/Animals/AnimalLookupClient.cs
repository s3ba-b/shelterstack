using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShelterStack.Adoptions.Api.Animals;

/// <summary>What a pre-approval lookup of an animal established.</summary>
public enum AnimalLookupOutcome
{
    /// <summary>The animal was found; <see cref="AnimalLookupResult.Status"/> is its status.</summary>
    Found,

    /// <summary>ShelterStack.Animals.Api has no such animal <i>in the caller's tenant</i>.</summary>
    NotFound,

    /// <summary>The lookup could not be completed (the service is down, slow, or erroring).</summary>
    Unavailable,
}

/// <param name="Status">
/// The animal's status as ShelterStack.Animals.Api spells it ("Available", "MedicalHold", …),
/// kept as a string because this service does not own that enum. Null unless
/// <see cref="AnimalLookupOutcome.Found"/>.
/// </param>
public sealed record AnimalLookupResult(AnimalLookupOutcome Outcome, string? Status);

/// <summary>
/// Reads an animal from ShelterStack.Animals.Api so <c>approve</c> can fail fast on the common
/// mistake — approving an application for an animal that is on a medical hold, still in intake,
/// or already adopted — with a plain 400 instead of a round trip through the broker and out to
/// <c>NeedsAttention</c>.
/// <para>
/// This is a convenience, <b>not</b> the correctness guarantee. The lookup and the later event
/// are two separate moments, so the animal's status can change in between; the authoritative
/// check stays in ShelterStack.Animals.Api, and the compensating
/// <c>AnimalStatusChangeRejected</c> path is what actually keeps an approval from being lost.
/// An <see cref="AnimalLookupOutcome.Unavailable"/> result therefore does not block the
/// approval — falling back to the compensating path is better than refusing to work at all
/// because a neighbouring service is briefly down.
/// </para>
/// <para>
/// The caller's own bearer token is forwarded rather than a service credential, so the lookup
/// is scoped by the same <c>tenant_id</c> claim the caller is acting under: an animal id from
/// another tenant comes back 404 here exactly as it would to that caller directly.
/// </para>
/// </summary>
public sealed class AnimalLookupClient(HttpClient httpClient, ILogger<AnimalLookupClient> logger)
{
    /// <summary>
    /// The statuses ShelterStack.Animals.Api's transition table allows a move to <c>Adopted</c>
    /// from. A local restatement of policy that service owns — kept deliberately small, and
    /// only ever used to produce a friendlier error, never to authorise the move.
    /// </summary>
    private static readonly HashSet<string> AdoptableStatuses = new(StringComparer.Ordinal)
    {
        "Available",
        "Fostered",
    };

    public static bool IsAdoptable(string status) => AdoptableStatuses.Contains(status);

    public async Task<AnimalLookupResult> LookUpAsync(
        Guid animalId,
        string? callerAuthorization,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{animalId}");
            if (
                !string.IsNullOrWhiteSpace(callerAuthorization)
                && AuthenticationHeaderValue.TryParse(callerAuthorization, out var authorization)
            )
            {
                request.Headers.Authorization = authorization;
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new AnimalLookupResult(AnimalLookupOutcome.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Animal lookup for {AnimalId} returned {StatusCode}; skipping the pre-check.",
                    animalId,
                    (int)response.StatusCode
                );
                return new AnimalLookupResult(AnimalLookupOutcome.Unavailable, null);
            }

            var animal = await response.Content.ReadFromJsonAsync<AnimalView>(
                SerializerOptions,
                cancellationToken
            );

            return animal?.Status is { } status
                ? new AnimalLookupResult(AnimalLookupOutcome.Found, status)
                : new AnimalLookupResult(AnimalLookupOutcome.Unavailable, null);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(
                ex,
                "Could not reach ShelterStack.Animals.Api to pre-check animal {AnimalId}; skipping the pre-check.",
                animalId
            );
            return new AnimalLookupResult(AnimalLookupOutcome.Unavailable, null);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>The one field of the animal resource this service needs.</summary>
    private sealed record AnimalView([property: JsonPropertyName("status")] string? Status);
}
