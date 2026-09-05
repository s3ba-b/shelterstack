extern alias identity;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ShelterStack.Animals.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;
using IdentityProgram = identity::Program;
using LoginRequest = identity::ShelterStack.Identity.Api.Auth.LoginRequest;
using LoginResponse = identity::ShelterStack.Identity.Api.Auth.LoginResponse;

namespace ShelterStack.IsolationTests;

/// <summary>
/// The end-to-end sibling of <see cref="CrossTenantIsolationTests"/>: where that test mints its
/// own token signed with the Animals host's key, this one proves the <em>auth pipeline itself</em>
/// can't be tricked into leaking cross-tenant data. It boots both real DI-wired hosts — the
/// Identity API and the Animals API — over HTTP against their own Postgres containers, logs in as
/// a seeded Tenant A user through the actual <c>POST /login</c> endpoint to obtain a genuine JWT
/// (not a hand-crafted one), then calls the Animals API with that token. A token issued for one
/// tenant must only ever return that tenant's animals. The two hosts share the same "Jwt"
/// signing key/issuer/audience (see their appsettings.json), which is exactly why a token minted
/// by Identity validates over in Animals. See CHARTER.md's cross-tenant risk mitigation for why
/// this runs through the real hosts rather than asserting at the data layer.
/// </summary>
public sealed class AuthPipelineCrossTenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _identityDb = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("identitydb")
        .Build();

    private readonly PostgreSqlContainer _animalsDb = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("shelterstackdb")
        .Build();

    private WebApplicationFactory<IdentityProgram> _identity = null!;
    private WebApplicationFactory<Program> _animals = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_identityDb.StartAsync(), _animalsDb.StartAsync());

        // See CrossTenantIsolationTests for why the connection strings are supplied via env var
        // and the hosts run as Production. The two services read distinct connection-string keys,
        // so they don't collide; the assembly-wide DisableTestParallelization (see AssemblyInfo)
        // keeps these globals from racing with the M0 test's shelterstackdb value.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__identitydb",
            _identityDb.GetConnectionString()
        );
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__shelterstackdb",
            _animalsDb.GetConnectionString()
        );

        _identity = new WebApplicationFactory<IdentityProgram>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );
        _animals = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );
    }

    public async Task DisposeAsync()
    {
        _animals.Dispose();
        _identity.Dispose();
        await _animalsDb.DisposeAsync();
        await _identityDb.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__shelterstackdb", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__identitydb", null);
    }

    [Fact]
    public async Task TokenIssuedForOneTenant_OnlySeesThatTenantsAnimalsThroughTheFullPipeline()
    {
        using var identityClient = _identity.CreateClient();
        using var animalsClient = _animals.CreateClient();

        // Real token from the real login endpoint — not a hand-crafted JWT.
        var northsideToken = await LoginAsync(identityClient, "admin@northside.example");
        var northsideAnimals = await GetAnimalsAsync(animalsClient, northsideToken);
        Assert.Equal(DemoAnimals.PerTenant, northsideAnimals.Length);
        Assert.Contains(northsideAnimals, a => a.Name == "Buddy");
        Assert.DoesNotContain(northsideAnimals, a => a.Name == "Whiskers");

        // A token genuinely issued for Riverside must never surface Northside's "Buddy", and vice
        // versa — the cross-tenant leak this whole suite exists to catch, here proven end to end
        // (Identity login → JWT → Animals request) rather than at the data layer.
        var riversideToken = await LoginAsync(identityClient, "admin@riverside.example");
        var riversideAnimals = await GetAnimalsAsync(animalsClient, riversideToken);
        Assert.Equal(DemoAnimals.PerTenant, riversideAnimals.Length);
        Assert.Contains(riversideAnimals, a => a.Name == "Whiskers");
        Assert.DoesNotContain(riversideAnimals, a => a.Name == "Buddy");
    }

    // All seeded demo users share the same password (see Identity's SeedDemoDataAsync).
    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/login",
            new LoginRequest(email, "Demo123!")
        );
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        return body!.AccessToken;
    }

    private static async Task<AnimalDto[]> GetAnimalsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AnimalDto[]>() ?? [];
    }

    private sealed record AnimalDto(Guid Id, string Name);
}
