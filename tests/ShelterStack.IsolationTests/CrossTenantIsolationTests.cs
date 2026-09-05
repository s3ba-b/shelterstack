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
using ShelterStack.Animals.Api.Auth;
using ShelterStack.Animals.Api.Data;
using ShelterStack.Animals.Api.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ShelterStack.IsolationTests;

/// <summary>
/// Drives the real Animals API host (Program.cs, unchanged) over HTTP against a real
/// Postgres container, asserting that a request authenticated as one tenant never sees
/// another tenant's rows. The tenant now comes from the validated <c>tenant_id</c> claim of a
/// JWT bearer token (replacing the M0 <c>X-Tenant-Id</c> header), so the tests mint tokens
/// signed with the host's own configured key. See CHARTER.md's cross-tenant risk mitigation
/// for why this goes through the actual DI-wired host rather than constructing the DbContext.
/// </summary>
public sealed class CrossTenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("shelterstackdb")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Program.cs's AddNpgsqlDataSource resolves "ConnectionStrings:shelterstackdb" lazily
        // (the first time the data source is requested, during startup seeding) rather than
        // at configuration-build time, so the env var just needs to be in place before that —
        // ConfigureAppConfiguration on WithWebHostBuilder doesn't reliably layer over a
        // ConfigurationManager already populated by WebApplication.CreateBuilder for the
        // minimal hosting model.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__shelterstackdb",
            _postgres.GetConnectionString()
        );

        // Run as Production, not the WebApplicationFactory default of Development: ASP.NET
        // Core's DI scope-validation (on by default in Development) would turn a pooled-vs-
        // scoped DbContext regression into a startup crash, masking the actual failure mode —
        // a silent cross-tenant data leak — that this test exists to catch. Per CHARTER.md,
        // that crash isn't a safety net we get in Production, so the test shouldn't lean on it.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__shelterstackdb", null);
    }

    [Fact]
    public async Task TenantRequests_OnlySeeTheirOwnAnimals()
    {
        using var client = _factory.CreateClient();

        var northsideAnimals = await GetAnimalsAsync(client, DemoTenants.Northside);
        Assert.Equal(DemoAnimals.PerTenant, northsideAnimals.Length);
        Assert.Contains(northsideAnimals, a => a.Name == "Buddy");
        Assert.DoesNotContain(northsideAnimals, a => a.Name == "Whiskers");

        // Same HttpClient/host as the Northside call above: a DbContext registered pooled
        // (AddDbContextPool) instead of scoped (AddDbContext) would hand this request the
        // pooled instance still bound to Northside's tenant context, returning Northside's
        // animals again instead of Riverside's own.
        var riversideAnimals = await GetAnimalsAsync(client, DemoTenants.Riverside);
        Assert.Equal(DemoAnimals.PerTenant, riversideAnimals.Length);
        Assert.Contains(riversideAnimals, a => a.Name == "Whiskers");
        Assert.DoesNotContain(riversideAnimals, a => a.Name == "Buddy");
    }

    [Fact]
    public async Task RequestWithoutToken_IsRejected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestFromVolunteer_IsForbidden()
    {
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(DemoTenants.Northside, "Volunteer")
        );

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnimalCreatedByOneTenant_IsInvisibleToAnother_AcrossTheRicherEntity()
    {
        using var client = _factory.CreateClient();

        // Create a fully-populated animal as Northside, exercising every new field on the way in.
        var created = await CreateAnimalAsync(
            client,
            DemoTenants.Northside,
            new CreateAnimalRequest(
                Name: "Rex",
                Species: AnimalSpecies.Dog,
                Breed: "German Shepherd",
                Sex: AnimalSex.Male,
                DateOfBirth: new DateOnly(2020, 1, 15),
                Description: "Energetic; needs a yard."
            )
        );

        // Northside reads it back by id with every field intact (and enums as readable names).
        using var northsideFetch = await GetAnimalAsync(client, DemoTenants.Northside, created.Id);
        Assert.Equal(HttpStatusCode.OK, northsideFetch.StatusCode);
        var northsideView = await northsideFetch.Content.ReadFromJsonAsync<AnimalDto>();
        Assert.NotNull(northsideView);
        Assert.Equal("Rex", northsideView!.Name);
        Assert.Equal("Dog", northsideView.Species);
        Assert.Equal("German Shepherd", northsideView.Breed);
        Assert.Equal("Male", northsideView.Sex);
        Assert.Equal(new DateOnly(2020, 1, 15), northsideView.DateOfBirth);

        // It appears in Northside's list alongside its seeded animals.
        var northsideList = await GetAnimalsAsync(client, DemoTenants.Northside);
        Assert.Contains(northsideList, a => a.Name == "Rex");

        // Riverside must never see Rex: not by id — a cross-tenant id is a 404, indistinguishable
        // from a row that does not exist...
        using var riversideFetch = await GetAnimalAsync(client, DemoTenants.Riverside, created.Id);
        Assert.Equal(HttpStatusCode.NotFound, riversideFetch.StatusCode);

        // ...and not in its list, which still holds only its own seeded animals.
        var riversideList = await GetAnimalsAsync(client, DemoTenants.Riverside);
        Assert.DoesNotContain(riversideList, a => a.Name == "Rex");
        Assert.Contains(riversideList, a => a.Name == "Whiskers");

        // The write path is scoped too: Riverside cannot update Northside's animal — the query
        // filter turns the lookup into a 404 rather than letting the mutation land.
        using var riversideUpdate = await UpdateAnimalAsync(
            client,
            DemoTenants.Riverside,
            created.Id,
            new UpdateAnimalRequest(
                Name: "Hacked",
                Species: AnimalSpecies.Cat,
                Breed: null,
                Sex: AnimalSex.Unknown,
                DateOfBirth: null,
                Description: null
            )
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideUpdate.StatusCode);

        // And Northside's copy is untouched by that attempt.
        using var afterAttack = await GetAnimalAsync(client, DemoTenants.Northside, created.Id);
        var stillRex = await afterAttack.Content.ReadFromJsonAsync<AnimalDto>();
        Assert.Equal("Rex", stillRex!.Name);
    }

    [Fact]
    public async Task AnimalStatus_IsIsolatedAcrossTenants()
    {
        using var client = _factory.CreateClient();

        var created = await CreateAnimalAsync(
            client,
            DemoTenants.Northside,
            new CreateAnimalRequest(
                Name: "Milo",
                Species: AnimalSpecies.Dog,
                Breed: null,
                Sex: AnimalSex.Unknown,
                DateOfBirth: null,
                Description: null
            )
        );

        // Riverside cannot move Northside's animal to a new status — the query filter turns the
        // lookup into a 404 rather than letting the mutation land.
        using var riversideChange = await ChangeStatusAsync(
            client,
            DemoTenants.Riverside,
            created.Id,
            AnimalStatus.Available
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideChange.StatusCode);

        // Nor can Riverside read Northside's status history for that animal.
        using var riversideHistory = await GetStatusHistoryAsync(
            client,
            DemoTenants.Riverside,
            created.Id
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideHistory.StatusCode);

        // Northside's own legal transition succeeds and is recorded.
        using var northsideChange = await ChangeStatusAsync(
            client,
            DemoTenants.Northside,
            created.Id,
            AnimalStatus.Available
        );
        Assert.Equal(HttpStatusCode.OK, northsideChange.StatusCode);

        using var northsideHistory = await GetStatusHistoryAsync(
            client,
            DemoTenants.Northside,
            created.Id
        );
        Assert.Equal(HttpStatusCode.OK, northsideHistory.StatusCode);
        var history = await northsideHistory.Content.ReadFromJsonAsync<StatusHistoryEntryDto[]>();
        Assert.NotNull(history);
        Assert.Single(history!);
        Assert.Equal("Available", history![0].Status);
    }

    [Fact]
    public async Task AnimalStatus_RejectsIllegalTransitions()
    {
        using var client = _factory.CreateClient();

        var created = await CreateAnimalAsync(
            client,
            DemoTenants.Northside,
            new CreateAnimalRequest(
                Name: "Daisy",
                Species: AnimalSpecies.Cat,
                Breed: null,
                Sex: AnimalSex.Unknown,
                DateOfBirth: null,
                Description: null
            )
        );

        // A freshly created animal starts at Intake; Intake -> Adopted skips Available entirely
        // and must be rejected.
        using var illegalChange = await ChangeStatusAsync(
            client,
            DemoTenants.Northside,
            created.Id,
            AnimalStatus.Adopted
        );
        Assert.Equal(HttpStatusCode.BadRequest, illegalChange.StatusCode);

        // The rejected attempt left no history row and the animal's status unchanged.
        using var history = await GetStatusHistoryAsync(client, DemoTenants.Northside, created.Id);
        var entries = await history.Content.ReadFromJsonAsync<StatusHistoryEntryDto[]>();
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task IntakeRecords_AreIsolatedAcrossTenants()
    {
        using var client = _factory.CreateClient();

        var northsideAnimal = await CreateAnimalAsync(
            client,
            DemoTenants.Northside,
            new CreateAnimalRequest(
                Name: "Pepper",
                Species: AnimalSpecies.Dog,
                Breed: null,
                Sex: AnimalSex.Unknown,
                DateOfBirth: null,
                Description: null
            )
        );

        // Failure mode 1: Riverside cannot attach an intake record to Northside's animal — the
        // query filter turns the Animal lookup into a 404 rather than letting the record land.
        using var riversideIntake = await RecordIntakeAsync(
            client,
            DemoTenants.Riverside,
            northsideAnimal.Id,
            new CreateIntakeRecordRequest(
                IntakeDate: new DateOnly(2026, 1, 5),
                IntakeType: IntakeType.Stray,
                Notes: "Attempted cross-tenant write."
            )
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideIntake.StatusCode);

        // Northside's own intake for that animal succeeds and is recorded.
        using var northsideIntake = await RecordIntakeAsync(
            client,
            DemoTenants.Northside,
            northsideAnimal.Id,
            new CreateIntakeRecordRequest(
                IntakeDate: new DateOnly(2026, 1, 5),
                IntakeType: IntakeType.Stray,
                Notes: "Found near the river trail."
            )
        );
        Assert.Equal(HttpStatusCode.Created, northsideIntake.StatusCode);

        // Failure mode 2: Riverside cannot read Northside's intake history for that animal —
        // a 404, indistinguishable from an animal that does not exist.
        using var riversideHistory = await GetIntakeHistoryAsync(
            client,
            DemoTenants.Riverside,
            northsideAnimal.Id
        );
        Assert.Equal(HttpStatusCode.NotFound, riversideHistory.StatusCode);

        // Northside reads its own history back, unaffected by the rejected cross-tenant attempt.
        using var northsideHistory = await GetIntakeHistoryAsync(
            client,
            DemoTenants.Northside,
            northsideAnimal.Id
        );
        Assert.Equal(HttpStatusCode.OK, northsideHistory.StatusCode);
        var history = await northsideHistory.Content.ReadFromJsonAsync<IntakeRecordEntryDto[]>();
        Assert.NotNull(history);
        Assert.Single(history!);
        Assert.Equal("Stray", history![0].IntakeType);
        Assert.Equal("Found near the river trail.", history[0].Notes);
    }

    private async Task<HttpResponseMessage> RecordIntakeAsync(
        HttpClient client,
        Guid tenantId,
        Guid animalId,
        CreateIntakeRecordRequest body
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{animalId}/intake")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.StaffRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetIntakeHistoryAsync(
        HttpClient client,
        Guid tenantId,
        Guid animalId
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{animalId}/intake-history");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.AdminRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ChangeStatusAsync(
        HttpClient client,
        Guid tenantId,
        Guid animalId,
        AnimalStatus status
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{animalId}/status")
        {
            Content = JsonContent.Create(new { Status = status.ToString() }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.StaffRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetStatusHistoryAsync(
        HttpClient client,
        Guid tenantId,
        Guid animalId
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{animalId}/status-history");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.AdminRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<AnimalDto> CreateAnimalAsync(
        HttpClient client,
        Guid tenantId,
        CreateAnimalRequest body
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.StaffRole)
        );

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AnimalDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private async Task<HttpResponseMessage> GetAnimalAsync(
        HttpClient client,
        Guid tenantId,
        Guid id
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.AdminRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> UpdateAnimalAsync(
        HttpClient client,
        Guid tenantId,
        Guid id,
        UpdateAnimalRequest body
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/{id}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.StaffRole)
        );

        return await client.SendAsync(request);
    }

    private async Task<AnimalDto[]> GetAnimalsAsync(HttpClient client, Guid tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueToken(tenantId, TokenAuth.AdminRole)
        );

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AnimalDto[]>() ?? [];
    }

    // Mints a token signed with the host's own configured key/issuer/audience, so it validates
    // through the exact same TokenValidationParameters the API uses — the auth-pipeline analogue
    // of LoginEndpointTests reading the key back to verify an issued token.
    private string IssueToken(Guid tenantId, string role)
    {
        var jwt = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: new[]
            {
                new Claim(TokenAuth.TenantIdClaim, tenantId.ToString()),
                new Claim(TokenAuth.RoleClaim, role),
            },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Enums arrive as their readable names (the host configures JsonStringEnumConverter), so the
    // Species/Sex fields are typed as string here to assert on exactly what the API emits.
    private sealed record AnimalDto(
        Guid Id,
        string Name,
        string Species,
        string? Breed,
        string Sex,
        DateOnly? DateOfBirth,
        string? Description,
        string Status
    );

    private sealed record StatusHistoryEntryDto(
        Guid Id,
        string Status,
        DateTimeOffset ChangedAtUtc
    );

    private sealed record IntakeRecordEntryDto(
        Guid Id,
        DateOnly IntakeDate,
        string IntakeType,
        string? Notes
    );
}
