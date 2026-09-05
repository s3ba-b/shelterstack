using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ShelterStack.Animals.Api;
using ShelterStack.Animals.Api.Auth;
using ShelterStack.Animals.Api.Data;
using ShelterStack.Animals.Api.Messaging;
using ShelterStack.Animals.Api.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Serialize the Species/Sex enums as their names ("Dog", "Female") rather than integers, so
// the resource shape stays readable and decoupled from the enum's declaration order.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

// Connect to the PostgreSQL "shelterstackdb" resource via Aspire service discovery,
// registering an NpgsqlDataSource plus a health check that proves the connection.
builder.AddNpgsqlDataSource("shelterstackdb");

// Unpooled by design: AnimalsDbContext takes the per-request ITenantContext as a
// constructor dependency, and DbContext pooling reuses instances (and whatever
// scoped service they captured) across unrelated requests' scopes — exactly the
// kind of cross-tenant leak this milestone exists to prevent.
builder.Services.AddDbContext<AnimalsDbContext>(
    (sp, options) => options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
);

builder.Services.AddHttpContextAccessor();

// Validate the JWT bearer tokens issued by ShelterStack.Identity.Api against the same signing
// key/issuer/audience (the "Jwt" section). Configure<JwtOptions> also exposes the values to
// integration tests via IOptions; the local snapshot is what AddJwtBearer needs at startup.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions =
    builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the custom claims ("role", "tenant_id") under their original names instead of
        // remapping "role" to the long ClaimTypes.Role URI — matches how the tokens are issued,
        // and RoleClaimType below points RequireRole at that same "role" claim.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)
            ),
            ValidateLifetime = true,
            RoleClaimType = TokenAuth.RoleClaim,
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        TokenAuth.StaffOrAdminPolicy,
        policy => policy.RequireRole(TokenAuth.AdminRole, TokenAuth.StaffRole)
    )
);

// Tenant resolution now comes from the authenticated token's tenant_id claim (replacing the
// M0 X-Tenant-Id header). The ITenantContext contract and every query filter built on it are
// unchanged — only the source of the tenant id moved.
builder.Services.AddScoped<ITenantContext, ClaimsTenantContext>();

// The broker leg of the adoption flow. AddRabbitMQClient resolves the Aspire "messaging"
// resource and registers the IConnection; AdoptionApprovedConsumer subscribes in the background
// and retries rather than failing startup, so this host still boots and serves HTTP when no
// broker is configured (as in the API-level tests).
builder.AddRabbitMQClient("messaging");
builder.Services.AddSingleton<EventPublisher>();
builder.Services.AddScoped<AdoptionApprovedHandler>();
builder.Services.AddHostedService<AdoptionApprovedConsumer>();

var app = builder.Build();

app.MapDefaultEndpoints();

await SeedDemoTenantsAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();

// Trivial liveness/ping endpoint (anonymous), reachable through the gateway at /animals/ping.
app.MapGet("/ping", () => Results.Ok(new { service = "animals-api", status = "ok" }));

// Tenant-scoped animal CRUD. Every route is restricted to admins and staff (volunteers get
// 403), and every query rides the EF Core global query filter, so a caller only ever reads or
// writes their own tenant's animals — no explicit per-call TenantId filtering. Reachable
// through the gateway under /animals.
var animals = app.MapGroup("").RequireAuthorization(TokenAuth.StaffOrAdminPolicy);

// List the caller's animals.
animals.MapGet(
    "/",
    async (AnimalsDbContext db) =>
    {
        var results = await db
            .Animals.OrderBy(a => a.Name)
            .Select(a => AnimalResponse.From(a))
            .ToListAsync();

        return Results.Ok(results);
    }
);

// Fetch one animal by id. The query filter turns a cross-tenant id into a 404, exactly as if
// the row did not exist — another tenant's animal is never distinguishable from a missing one.
animals.MapGet(
    "/{id:guid}",
    async (Guid id, AnimalsDbContext db) =>
    {
        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Id == id);

        return animal is null ? Results.NotFound() : Results.Ok(AnimalResponse.From(animal));
    }
);

// Create an animal in the caller's tenant. TenantId comes from the resolved ITenantContext
// (the token), never the request body, so a caller cannot plant a row in another tenant.
animals.MapPost(
    "/",
    async (CreateAnimalRequest request, AnimalsDbContext db, ITenantContext tenant) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(request.Name)] = ["Name is required."] }
            );
        }

        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = request.Name,
            Species = request.Species,
            Breed = request.Breed,
            Sex = request.Sex,
            DateOfBirth = request.DateOfBirth,
            Description = request.Description,
        };

        db.Animals.Add(animal);
        await db.SaveChangesAsync();

        return Results.Created($"/{animal.Id}", AnimalResponse.From(animal));
    }
);

// Update an animal. The query filter scopes the lookup to the caller's tenant, so an attempt
// to update another tenant's animal resolves to NotFound rather than mutating their data.
animals.MapPut(
    "/{id:guid}",
    async (Guid id, UpdateAnimalRequest request, AnimalsDbContext db) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(request.Name)] = ["Name is required."] }
            );
        }

        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Id == id);
        if (animal is null)
        {
            return Results.NotFound();
        }

        animal.Name = request.Name;
        animal.Species = request.Species;
        animal.Breed = request.Breed;
        animal.Sex = request.Sex;
        animal.DateOfBirth = request.DateOfBirth;
        animal.Description = request.Description;

        await db.SaveChangesAsync();

        return Results.Ok(AnimalResponse.From(animal));
    }
);

// Move an animal to a new status, rejecting illegal transitions (e.g. Adopted straight back to
// Intake) with a 400 and recording every accepted change as a status-history row. The query
// filter scopes the lookup to the caller's tenant, so a cross-tenant id is a 404 exactly as for
// the other write paths.
animals.MapPost(
    "/{id:guid}/status",
    async (
        Guid id,
        ChangeAnimalStatusRequest request,
        AnimalsDbContext db,
        ITenantContext tenant
    ) =>
    {
        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Id == id);
        if (animal is null)
        {
            return Results.NotFound();
        }

        if (!AnimalStatusTransitions.IsAllowed(animal.Status, request.Status))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(request.Status)] =
                    [
                        $"Cannot move an animal from {animal.Status} to {request.Status}.",
                    ],
                }
            );
        }

        animal.Status = request.Status;
        db.AnimalStatusHistory.Add(
            new AnimalStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                AnimalId = animal.Id,
                Status = request.Status,
                ChangedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await db.SaveChangesAsync();

        return Results.Ok(AnimalResponse.From(animal));
    }
);

// List an animal's status-change history, oldest first. Tenant-scoped via the query filter on
// AnimalStatusHistory, and the Animal lookup above means a cross-tenant animal id is a 404
// rather than an empty history list — the two are not the same thing to the caller.
animals.MapGet(
    "/{id:guid}/status-history",
    async (Guid id, AnimalsDbContext db) =>
    {
        var animalExists = await db.Animals.AnyAsync(a => a.Id == id);
        if (!animalExists)
        {
            return Results.NotFound();
        }

        var history = await db
            .AnimalStatusHistory.Where(h => h.AnimalId == id)
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => AnimalStatusHistoryResponse.From(h))
            .ToListAsync();

        return Results.Ok(history);
    }
);

// Record an intake for an animal (stray, surrender, transfer-in, etc.). An animal can have more
// than one intake over its life (e.g. returned, then re-intaken), so this is additive, not a
// replace. The query filter scopes the Animal lookup to the caller's tenant, so a cross-tenant
// animal id is a 404 rather than letting the new record attach to someone else's animal.
animals.MapPost(
    "/{id:guid}/intake",
    async (
        Guid id,
        CreateIntakeRecordRequest request,
        AnimalsDbContext db,
        ITenantContext tenant
    ) =>
    {
        var animalExists = await db.Animals.AnyAsync(a => a.Id == id);
        if (!animalExists)
        {
            return Results.NotFound();
        }

        var record = new IntakeRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            AnimalId = id,
            IntakeDate = request.IntakeDate,
            IntakeType = request.IntakeType,
            Notes = request.Notes,
        };

        db.IntakeRecords.Add(record);
        await db.SaveChangesAsync();

        return Results.Created($"/{id}/intake", IntakeRecordResponse.From(record));
    }
);

// List an animal's intake history, oldest first. Tenant-scoped via the query filter on
// IntakeRecords, and the Animal lookup above means a cross-tenant animal id is a 404 rather
// than an empty history list — the two are not the same thing to the caller.
animals.MapGet(
    "/{id:guid}/intake-history",
    async (Guid id, AnimalsDbContext db) =>
    {
        var animalExists = await db.Animals.AnyAsync(a => a.Id == id);
        if (!animalExists)
        {
            return Results.NotFound();
        }

        var history = await db
            .IntakeRecords.Where(r => r.AnimalId == id)
            .OrderBy(r => r.IntakeDate)
            .Select(r => IntakeRecordResponse.From(r))
            .ToListAsync();

        return Results.Ok(history);
    }
);

app.Run();

static async Task SeedDemoTenantsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AnimalsDbContext>>();

    // Filters apply to queries, not inserts, so a single context (with any tenant)
    // can migrate the schema and seed rows across multiple demo tenants.
    await using var db = new AnimalsDbContext(options, new StaticTenantContext(Guid.Empty));
    await db.Database.MigrateAsync();

    if (await db.Animals.IgnoreQueryFilters().AnyAsync())
    {
        return;
    }

    // Ids are deterministic, not freshly generated: ShelterStack.Adoptions.Api seeds its demo
    // applications against these same animal ids from its own database. See DemoAnimals.
    db.Animals.AddRange(DemoAnimals.All());

    await db.SaveChangesAsync();
}

// Makes the top-level-statement Program class public so the isolation tests' (and any
// future integration tests') WebApplicationFactory<Program> can boot the real DI-wired
// host instead of a hand-rolled stand-in.
public partial class Program;
