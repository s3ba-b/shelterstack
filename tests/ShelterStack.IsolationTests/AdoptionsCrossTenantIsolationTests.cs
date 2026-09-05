extern alias adoptions;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShelterStack.Animals.Api;
using ShelterStack.Animals.Api.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;
using AdoptionsApi = adoptions::ShelterStack.Adoptions.Api;
using AdoptionsAuth = adoptions::ShelterStack.Adoptions.Api.Auth;
using AdoptionsData = adoptions::ShelterStack.Adoptions.Api.Data;
using AdoptionsMessaging = adoptions::ShelterStack.Adoptions.Api.Messaging;
using AdoptionsProgram = adoptions::Program;
using AdoptionsTenancy = adoptions::ShelterStack.Adoptions.Api.Tenancy;

namespace ShelterStack.IsolationTests;

/// <summary>
/// The adoption slice's cross-tenant gate. Both real hosts — Adoptions and Animals — are driven
/// over HTTP and over a real broker exactly as they run under Aspire (see
/// <see cref="AdoptionsFlowFixture"/>), so what is asserted is the deployed behaviour rather
/// than a stand-in.
/// <para>
/// This covers two boundaries, not one. The HTTP boundary is the familiar one: an application
/// belonging to another tenant is a 404 whether it is read, approved, or rejected. The broker
/// boundary is new with M4 and is the reason this file exists — approving publishes an event
/// carrying its own <c>TenantId</c>, and if a consumer treated that as a trusted claim rather
/// than pushing it through the same query filters, a message would become a way to reach across
/// tenants that no HTTP request can. That is the exact shape of the <c>X-Tenant-Id</c> leak in
/// issue #106, one transport over.
/// </para>
/// </summary>
public sealed class AdoptionsCrossTenantIsolationTests(AdoptionsFlowFixture shelter)
    : IClassFixture<AdoptionsFlowFixture>
{
    private static readonly Guid Northside = AdoptionsTenancy.DemoTenants.Northside;
    private static readonly Guid Riverside = AdoptionsTenancy.DemoTenants.Riverside;

    [Fact]
    public async Task RequestWithoutToken_IsRejected()
    {
        using var response = await shelter.AdoptionsClient.GetAsync("/applications/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestFromVolunteer_IsForbidden()
    {
        // Applications carry applicant names, addresses and phone numbers, so volunteers are shut
        // out of this service entirely rather than given read access.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/applications/");
        request.Headers.Authorization = shelter.Bearer(Northside, "Volunteer");

        using var response = await shelter.AdoptionsClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApplicationsAreIsolatedAcrossTenants_OnEveryRoute()
    {
        var animal = await shelter.CreateAvailableAnimalAsync(Northside, "Rex");
        var application = await shelter.CreateApplicationAsync(
            Northside,
            animal.Id,
            "Anna Kowalska"
        );

        // Read: a cross-tenant id is a 404, indistinguishable from a row that does not exist.
        using var riversideRead = await shelter.GetApplicationAsync(Riverside, application.Id);
        Assert.Equal(HttpStatusCode.NotFound, riversideRead.StatusCode);

        // List: Northside's application is not in Riverside's list...
        var riversideList = await shelter.ListApplicationsAsync(Riverside);
        Assert.DoesNotContain(riversideList, a => a.Id == application.Id);

        // ...and is in Northside's own.
        var northsideList = await shelter.ListApplicationsAsync(Northside);
        Assert.Contains(northsideList, a => a.Id == application.Id);

        // Write: neither decision route lets Riverside touch it — the query filter turns both
        // lookups into a 404 rather than letting the mutation land.
        using var riversideApprove = await shelter.ApproveAsync(Riverside, application.Id);
        Assert.Equal(HttpStatusCode.NotFound, riversideApprove.StatusCode);

        using var riversideReject = await shelter.RejectAsync(
            Riverside,
            application.Id,
            "Not ours to reject."
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideReject.StatusCode);

        // And Northside's copy is untouched by those attempts.
        var afterAttack = await shelter.ReadApplicationAsync(Northside, application.Id);
        Assert.Equal(nameof(AdoptionsData.AdoptionApplicationStatus.Submitted), afterAttack.Status);
        Assert.Null(afterAttack.StatusReason);
    }

    [Fact]
    public async Task ApprovingAnApplication_MovesTheAnimalToAdopted_OverTheBroker()
    {
        var animal = await shelter.CreateAvailableAnimalAsync(Northside, "Bailey");
        var application = await shelter.CreateApplicationAsync(
            Northside,
            animal.Id,
            "Marek Wiśniewski"
        );

        using var approve = await shelter.ApproveAsync(Northside, application.Id);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var approved = await approve.Content.ReadFromJsonAsync<ApplicationDto>();
        Assert.Equal(nameof(AdoptionsData.AdoptionApplicationStatus.Approved), approved!.Status);
        Assert.NotNull(approved.DecidedAtUtc);

        // The animal is moved by ShelterStack.Animals.Api consuming AdoptionApproved, not by the
        // request that returned above — so this is a wait, not an assertion on the response.
        await shelter.WaitUntilAsync(
            async () =>
                (await shelter.ReadAnimalAsync(Northside, animal.Id)).Status
                == nameof(AnimalStatus.Adopted),
            $"animal {animal.Id} to be moved to Adopted by the AdoptionApproved consumer"
        );

        // The move is recorded in the same status-history trail the HTTP status endpoint writes,
        // so an adoption that happened over the broker is not invisible in the audit trail.
        var history = await shelter.ReadStatusHistoryAsync(Northside, animal.Id);
        Assert.Contains(history, h => h.Status == nameof(AnimalStatus.Adopted));
    }

    [Fact]
    public async Task ApprovingAFosteredAnimal_MovesItToAdopted()
    {
        // Fostered -> Adopted is legal per the transition table: a foster carer adopting the
        // animal they are fostering is one of the commonest adoption routes there is.
        var animal = await shelter.CreateAvailableAnimalAsync(Riverside, "Boomer");
        await shelter.SetAnimalStatusAsync(Riverside, animal.Id, AnimalStatus.Fostered);

        var application = await shelter.CreateApplicationAsync(
            Riverside,
            animal.Id,
            "Adrian Sadowski"
        );

        using var approve = await shelter.ApproveAsync(Riverside, application.Id);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        await shelter.WaitUntilAsync(
            async () =>
                (await shelter.ReadAnimalAsync(Riverside, animal.Id)).Status
                == nameof(AnimalStatus.Adopted),
            $"fostered animal {animal.Id} to be moved to Adopted"
        );
    }

    [Fact]
    public async Task ApprovingAnAnimalOnMedicalHold_IsRefusedUpFront()
    {
        var animal = await shelter.CreateAvailableAnimalAsync(Northside, "Ruby");
        await shelter.SetAnimalStatusAsync(Northside, animal.Id, AnimalStatus.MedicalHold);

        var application = await shelter.CreateApplicationAsync(
            Northside,
            animal.Id,
            "Rafał Piotrowski"
        );

        // The pre-check makes the common case a clean 400 instead of a round trip out to
        // NeedsAttention, and the application is left open rather than half-decided.
        using var approve = await shelter.ApproveAsync(Northside, application.Id);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);

        var unchanged = await shelter.ReadApplicationAsync(Northside, application.Id);
        Assert.Equal(nameof(AdoptionsData.AdoptionApplicationStatus.Submitted), unchanged.Status);
        Assert.Null(unchanged.DecidedAtUtc);
    }

    [Fact]
    public async Task AnApprovalTheAnimalsServiceRefuses_LandsTheApplicationInNeedsAttention()
    {
        var animal = await shelter.CreateAvailableAnimalAsync(Northside, "Oscar");
        var application = await shelter.CreateApplicationAsync(
            Northside,
            animal.Id,
            "Ewa Szymańska"
        );
        await shelter.SetAnimalStatusAsync(Northside, animal.Id, AnimalStatus.MedicalHold);

        // The event is published directly rather than through approve, because approve's
        // pre-check would (correctly) refuse first. What is under test is the compensating path
        // behind it: the pre-check races by nature — the animal can go on a medical hold in the
        // moment between the lookup and the event being handled — and that race is only safe
        // because a refused transition comes back as AnimalStatusChangeRejected rather than
        // being dropped. Everything from here on is the real production path.
        await shelter.PublishAdoptionApprovedAsync(Northside, application.Id, animal.Id);

        await shelter.WaitUntilAsync(
            async () =>
                (await shelter.ReadApplicationAsync(Northside, application.Id)).Status
                == nameof(AdoptionsData.AdoptionApplicationStatus.NeedsAttention),
            $"application {application.Id} to land in NeedsAttention"
        );

        // The reason travels with it, so staff see why rather than just that something failed.
        var needsAttention = await shelter.ReadApplicationAsync(Northside, application.Id);
        Assert.Contains("MedicalHold", needsAttention.StatusReason);

        // And the animal is still on its medical hold — nothing was half-applied.
        var stillOnHold = await shelter.ReadAnimalAsync(Northside, animal.Id);
        Assert.Equal(nameof(AnimalStatus.MedicalHold), stillOnHold.Status);
    }

    [Fact]
    public async Task AnEventBearingAnotherTenantsId_CannotReachThatTenantsData()
    {
        // The failure mode: events carry their own TenantId, so a consumer that trusted it as a
        // transport-level claim instead of running it through the same query filters would make
        // the broker a way across the tenant boundary that no HTTP request can take.
        var northsideAnimal = await shelter.CreateAvailableAnimalAsync(Northside, "Buddy");
        var northsideApplication = await shelter.CreateApplicationAsync(
            Northside,
            northsideAnimal.Id,
            "Julia Nowak"
        );

        var riversideAnimal = await shelter.CreateAvailableAnimalAsync(Riverside, "Bella");
        var riversideApplication = await shelter.CreateApplicationAsync(
            Riverside,
            riversideAnimal.Id,
            "Jakub Michalski"
        );
        var riversideControl = await shelter.CreateApplicationAsync(
            Riverside,
            riversideAnimal.Id,
            "Natalia Adamczyk"
        );

        // Riverside's tenant id, Northside's animal and application. Nothing about the message is
        // malformed — this is precisely what a compromised or buggy publisher would emit.
        await shelter.PublishAdoptionApprovedAsync(
            Riverside,
            northsideApplication.Id,
            northsideAnimal.Id
        );

        // Proving a mutation did *not* happen needs a barrier, not a sleep. Both consumers take
        // one message at a time off a single durable queue in order, so once a message published
        // after the forged one has visibly been handled, the forged one is definitively done.
        await shelter.PublishAdoptionApprovedAsync(
            Riverside,
            riversideApplication.Id,
            riversideAnimal.Id
        );
        await shelter.WaitUntilAsync(
            async () =>
                (await shelter.ReadAnimalAsync(Riverside, riversideAnimal.Id)).Status
                == nameof(AnimalStatus.Adopted),
            "the barrier event behind the forged one to be handled"
        );

        // Northside's animal never moved: the forged message ran under Riverside's tenant, where
        // the query filter simply does not see that animal.
        var untouchedAnimal = await shelter.ReadAnimalAsync(Northside, northsideAnimal.Id);
        Assert.Equal(nameof(AnimalStatus.Available), untouchedAnimal.Status);

        var animalHistory = await shelter.ReadStatusHistoryAsync(Northside, northsideAnimal.Id);
        Assert.DoesNotContain(animalHistory, h => h.Status == nameof(AnimalStatus.Adopted));

        // The same must hold on the way back. Refusing the forged event published an
        // AnimalStatusChangeRejected carrying Riverside's tenant id and Northside's application
        // id; the compensating consumer must not be able to write NeedsAttention onto it either.
        // Barrier again, on the other queue this time.
        await shelter.PublishAnimalStatusChangeRejectedAsync(
            Riverside,
            riversideControl.Id,
            riversideAnimal.Id,
            "Barrier event."
        );
        await shelter.WaitUntilAsync(
            async () =>
                (await shelter.ReadApplicationAsync(Riverside, riversideControl.Id)).Status
                == nameof(AdoptionsData.AdoptionApplicationStatus.NeedsAttention),
            "the barrier rejection behind the forged one to be handled"
        );

        var untouchedApplication = await shelter.ReadApplicationAsync(
            Northside,
            northsideApplication.Id
        );
        Assert.Equal(
            nameof(AdoptionsData.AdoptionApplicationStatus.Submitted),
            untouchedApplication.Status
        );
        Assert.Null(untouchedApplication.StatusReason);
    }

    [Fact]
    public async Task ARejectedApplication_RecordsItsReason_AndCannotBeDecidedTwice()
    {
        var animal = await shelter.CreateAvailableAnimalAsync(Northside, "Bruno");
        var application = await shelter.CreateApplicationAsync(
            Northside,
            animal.Id,
            "Agnieszka Wójcik"
        );

        using var reject = await shelter.RejectAsync(
            Northside,
            application.Id,
            "A third-floor flat with no lift is not workable for a senior dog."
        );
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var rejected = await shelter.ReadApplicationAsync(Northside, application.Id);
        Assert.Equal(nameof(AdoptionsData.AdoptionApplicationStatus.Rejected), rejected.Status);
        Assert.Contains("third-floor flat", rejected.StatusReason);
        Assert.NotNull(rejected.DecidedAtUtc);

        // A decided application is not decided again — approving it after the fact would publish
        // an AdoptionApproved for an animal the shelter already turned this applicant down for.
        using var approveAfterReject = await shelter.ApproveAsync(Northside, application.Id);
        Assert.Equal(HttpStatusCode.BadRequest, approveAfterReject.StatusCode);

        // The animal is untouched by the rejection — it is still adoptable by someone else.
        var stillAvailable = await shelter.ReadAnimalAsync(Northside, animal.Id);
        Assert.Equal(nameof(AnimalStatus.Available), stillAvailable.Status);
    }

    [Fact]
    public async Task ApprovingAnApplicationForAnAnimalOfAnotherTenant_IsRefused()
    {
        // The two services hold separate databases with no foreign key between them, so an
        // application can name any animal id at all. The pre-check runs under the caller's own
        // token, so another tenant's animal comes back 404 there exactly as it would to the
        // caller directly — the application cannot be used as a lever onto it.
        var northsideAnimal = await shelter.CreateAvailableAnimalAsync(Northside, "Luna");

        var application = await shelter.CreateApplicationAsync(
            Riverside,
            northsideAnimal.Id,
            "Alicja Pawlak"
        );

        using var approve = await shelter.ApproveAsync(Riverside, application.Id);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);

        var northsideView = await shelter.ReadAnimalAsync(Northside, northsideAnimal.Id);
        Assert.Equal(nameof(AnimalStatus.Available), northsideView.Status);
    }
}

/// <summary>
/// Boots the two real hosts against real Postgres and RabbitMQ containers, and exposes the
/// request helpers the tests drive them with.
/// <para>
/// A class fixture rather than per-test setup, unlike <see cref="CrossTenantIsolationTests"/>:
/// this suite needs three containers <i>and</i> two hosts, and xUnit builds a fresh test-class
/// instance per fact, so per-test setup would pay that cost a dozen times over in CI. Nothing
/// is shared between the tests but the hosts themselves — each one creates the animals and
/// applications it needs — and facts within a class run one at a time, which is also what keeps
/// the broker-ordering barriers in the isolation test meaningful.
/// </para>
/// </summary>
public sealed class AdoptionsFlowFixture : IAsyncLifetime
{
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _animalsPostgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    )
        .WithDatabase("shelterstackdb")
        .Build();

    private readonly PostgreSqlContainer _adoptionsPostgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    )
        .WithDatabase("adoptionsdb")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4").Build();

    private WebApplicationFactory<Program> _animals = null!;
    private WebApplicationFactory<AdoptionsProgram> _adoptions = null!;

    public HttpClient AnimalsClient { get; private set; } = null!;

    public HttpClient AdoptionsClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _animalsPostgres.StartAsync(),
            _adoptionsPostgres.StartAsync(),
            _rabbitMq.StartAsync()
        );

        // Same lazy-configuration reasoning as CrossTenantIsolationTests: the connection strings
        // are read the first time each resource is resolved (during startup seeding, and when a
        // consumer first subscribes), so process-wide environment variables set before the hosts
        // boot are what reliably reach a ConfigurationManager built by WebApplication.CreateBuilder.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__shelterstackdb",
            _animalsPostgres.GetConnectionString()
        );
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__adoptionsdb",
            _adoptionsPostgres.GetConnectionString()
        );
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__messaging",
            _rabbitMq.GetConnectionString()
        );

        // Production rather than the WebApplicationFactory default of Development, for the reason
        // spelled out in CrossTenantIsolationTests: DI scope validation would turn a pooled-vs-
        // scoped DbContext regression into a startup crash and hide the leak this suite hunts.
        _animals = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );

        // Force the Animals host up (and seeded) before the Adoptions host, whose approve
        // pre-check calls into it through the handler wired below.
        AnimalsClient = _animals.CreateClient();

        _adoptions = new WebApplicationFactory<AdoptionsProgram>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");

            // The approve pre-check is a genuine HTTP call in production, resolved through Aspire
            // service discovery. In-process there is no such address, so the typed client's
            // primary handler is pointed at the Animals test server — the request, its forwarded
            // bearer token, and the JSON that comes back are all the real thing.
            b.ConfigureServices(services =>
                services
                    .AddHttpClient<AdoptionsApi.Animals.AnimalLookupClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _animals.Server.CreateHandler())
            );
        });

        AdoptionsClient = _adoptions.CreateClient();
    }

    public async Task DisposeAsync()
    {
        AnimalsClient.Dispose();
        AdoptionsClient.Dispose();
        _adoptions.Dispose();
        _animals.Dispose();

        await Task.WhenAll(
            _animalsPostgres.DisposeAsync().AsTask(),
            _adoptionsPostgres.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask()
        );

        Environment.SetEnvironmentVariable("ConnectionStrings__shelterstackdb", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__adoptionsdb", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__messaging", null);
    }

    // ---- broker helpers -------------------------------------------------------------------

    // Published through the service's own EventPublisher, against the same exchange and routing
    // key production uses, so these are indistinguishable on the wire from events a real
    // approval emits.
    public Task PublishAdoptionApprovedAsync(Guid tenantId, Guid applicationId, Guid animalId) =>
        _adoptions
            .Services.GetRequiredService<AdoptionsMessaging.EventPublisher>()
            .PublishAsync(
                new AdoptionsMessaging.AdoptionApproved(
                    tenantId,
                    applicationId,
                    animalId,
                    DateTimeOffset.UtcNow
                ),
                AdoptionsMessaging.ShelterStackEvents.AdoptionApprovedRoutingKey,
                CancellationToken.None
            );

    public Task PublishAnimalStatusChangeRejectedAsync(
        Guid tenantId,
        Guid applicationId,
        Guid animalId,
        string reason
    ) =>
        _adoptions
            .Services.GetRequiredService<AdoptionsMessaging.EventPublisher>()
            .PublishAsync(
                new AdoptionsMessaging.AnimalStatusChangeRejected(
                    tenantId,
                    applicationId,
                    animalId,
                    reason,
                    DateTimeOffset.UtcNow
                ),
                AdoptionsMessaging.ShelterStackEvents.AnimalStatusChangeRejectedRoutingKey,
                CancellationToken.None
            );

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. Event handling is asynchronous by design,
    /// so there is no response to assert on — but the wait is bounded, so a consumer that never
    /// acts fails the test rather than hanging CI.
    /// </summary>
    public async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + BrokerTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.Fail($"Timed out after {BrokerTimeout.TotalSeconds:0}s waiting for {because}.");
    }

    // ---- Animals API helpers --------------------------------------------------------------

    public async Task<AnimalDto> CreateAvailableAnimalAsync(Guid tenantId, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent.Create(
                new CreateAnimalRequest(
                    Name: name,
                    Species: AnimalSpecies.Dog,
                    Breed: null,
                    Sex: AnimalSex.Unknown,
                    DateOfBirth: null,
                    Description: null
                )
            ),
        };
        request.Headers.Authorization = Bearer(tenantId, StaffRole);

        using var response = await AnimalsClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<AnimalDto>();

        // A new animal starts at Intake; Available is the status an adoption actually applies from.
        await SetAnimalStatusAsync(tenantId, created!.Id, AnimalStatus.Available);

        return created with
        {
            Status = nameof(AnimalStatus.Available),
        };
    }

    public async Task SetAnimalStatusAsync(Guid tenantId, Guid animalId, AnimalStatus status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{animalId}/status")
        {
            Content = JsonContent.Create(new { Status = status.ToString() }),
        };
        request.Headers.Authorization = Bearer(tenantId, StaffRole);

        using var response = await AnimalsClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task<AnimalDto> ReadAnimalAsync(Guid tenantId, Guid animalId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{animalId}");
        request.Headers.Authorization = Bearer(tenantId, AdminRole);

        using var response = await AnimalsClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AnimalDto>())!;
    }

    public async Task<StatusHistoryDto[]> ReadStatusHistoryAsync(Guid tenantId, Guid animalId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{animalId}/status-history");
        request.Headers.Authorization = Bearer(tenantId, AdminRole);

        using var response = await AnimalsClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<StatusHistoryDto[]>() ?? [];
    }

    // ---- Adoptions API helpers ------------------------------------------------------------

    public async Task<ApplicationDto> CreateApplicationAsync(
        Guid tenantId,
        Guid animalId,
        string applicantName
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/applications/")
        {
            Content = JsonContent.Create(
                new AdoptionsApi.CreateAdoptionApplicationRequest(
                    AnimalId: animalId,
                    ApplicantName: applicantName,
                    ApplicantEmail: "applicant@example.com",
                    ApplicantPhone: null,
                    ApplicantAddress: null,
                    Notes: null
                )
            ),
        };
        request.Headers.Authorization = Bearer(tenantId, StaffRole);

        using var response = await AdoptionsClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ApplicationDto>())!;
    }

    public async Task<HttpResponseMessage> GetApplicationAsync(Guid tenantId, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/applications/{id}");
        request.Headers.Authorization = Bearer(tenantId, AdminRole);

        return await AdoptionsClient.SendAsync(request);
    }

    public async Task<ApplicationDto> ReadApplicationAsync(Guid tenantId, Guid id)
    {
        using var response = await GetApplicationAsync(tenantId, id);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApplicationDto>())!;
    }

    public async Task<ApplicationDto[]> ListApplicationsAsync(Guid tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/applications/");
        request.Headers.Authorization = Bearer(tenantId, AdminRole);

        using var response = await AdoptionsClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ApplicationDto[]>() ?? [];
    }

    public async Task<HttpResponseMessage> ApproveAsync(Guid tenantId, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/applications/{id}/approve");
        request.Headers.Authorization = Bearer(tenantId, StaffRole);

        return await AdoptionsClient.SendAsync(request);
    }

    public async Task<HttpResponseMessage> RejectAsync(Guid tenantId, Guid id, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/applications/{id}/reject")
        {
            Content = JsonContent.Create(new AdoptionsApi.RejectAdoptionApplicationRequest(reason)),
        };
        request.Headers.Authorization = Bearer(tenantId, StaffRole);

        return await AdoptionsClient.SendAsync(request);
    }

    // ---- auth ------------------------------------------------------------------------------

    private const string AdminRole = AdoptionsAuth.TokenAuth.AdminRole;
    private const string StaffRole = AdoptionsAuth.TokenAuth.StaffRole;

    /// <summary>
    /// Mints a token against the Adoptions host's configured key, issuer and audience. The same
    /// token authenticates against the Animals host, and has to: the approve pre-check forwards
    /// the caller's own bearer token to that service, so both validating it is the production
    /// arrangement rather than a shortcut taken here.
    /// </summary>
    public AuthenticationHeaderValue Bearer(Guid tenantId, string role)
    {
        var jwt = _adoptions
            .Services.GetRequiredService<IOptions<AdoptionsAuth.JwtOptions>>()
            .Value;

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims:
            [
                new Claim(AdoptionsAuth.TokenAuth.TenantIdClaim, tenantId.ToString()),
                new Claim(AdoptionsAuth.TokenAuth.RoleClaim, role),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials
        );

        return new AuthenticationHeaderValue(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(token)
        );
    }
}

// Enums arrive as their readable names (both hosts configure JsonStringEnumConverter), so these
// fields are typed as string to assert on exactly what the APIs emit.
public sealed record AnimalDto(Guid Id, string Name, string Status);

public sealed record StatusHistoryDto(Guid Id, string Status, DateTimeOffset ChangedAtUtc);

public sealed record ApplicationDto(
    Guid Id,
    Guid AnimalId,
    string ApplicantName,
    string Status,
    string? StatusReason,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc
);
