using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ShelterStack.Adoptions.Api;
using ShelterStack.Adoptions.Api.Animals;
using ShelterStack.Adoptions.Api.Auth;
using ShelterStack.Adoptions.Api.Data;
using ShelterStack.Adoptions.Api.Messaging;
using ShelterStack.Adoptions.Api.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Serialize the Status enum as its name ("Submitted", "NeedsAttention") rather than an integer,
// so the resource shape stays readable and decoupled from the enum's declaration order.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

// Connect to the PostgreSQL "adoptionsdb" resource via Aspire service discovery, registering an
// NpgsqlDataSource plus a health check that proves the connection. This service owns its own
// database — it references animals by id, never across a shared schema.
builder.AddNpgsqlDataSource("adoptionsdb");

// Unpooled by design: AdoptionsDbContext takes the per-request ITenantContext as a constructor
// dependency, and DbContext pooling reuses instances (and whatever scoped service they
// captured) across unrelated requests' scopes — exactly the kind of cross-tenant leak the
// project's isolation rule exists to prevent.
builder.Services.AddDbContext<AdoptionsDbContext>(
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

// Tenant resolution comes from the authenticated token's tenant_id claim — never a header, a
// route value, or a request body.
builder.Services.AddScoped<ITenantContext, ClaimsTenantContext>();

// Reads an animal from ShelterStack.Animals.Api so approve can fail fast on the common mistake
// (approving an animal on a medical hold). Resolved through Aspire service discovery; the
// address is overridable so tests can point it at a stubbed or in-memory host.
builder.Services.AddHttpClient<AnimalLookupClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Services:AnimalsApi:BaseAddress"] ?? "http://animals-api"
    )
);

// The broker leg of the adoption flow. AddRabbitMQClient resolves the Aspire "messaging"
// resource and registers the IConnection; AnimalStatusChangeRejectedConsumer subscribes in the
// background and retries rather than failing startup, so this host still boots and serves HTTP
// when no broker is configured (as in the API-level tests).
builder.AddRabbitMQClient("messaging");
builder.Services.AddSingleton<EventPublisher>();
builder.Services.AddScoped<AnimalStatusChangeRejectedHandler>();
builder.Services.AddHostedService<AnimalStatusChangeRejectedConsumer>();

var app = builder.Build();

app.MapDefaultEndpoints();

await SeedDemoTenantsAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();

// Trivial liveness/ping endpoint (anonymous), reachable through the gateway at /adoptions/ping.
app.MapGet("/ping", () => Results.Ok(new { service = "adoptions-api", status = "ok" }));

// Tenant-scoped adoption applications. Every route is restricted to admins and staff
// (volunteers get 403 — these records hold applicant personal data), and every query rides the
// EF Core global query filter, so a caller only ever reads or writes their own tenant's
// applications. Reachable through the gateway under /adoptions/applications.
var applications = app.MapGroup("/applications").RequireAuthorization(TokenAuth.StaffOrAdminPolicy);

// List the caller's applications, newest first.
applications.MapGet(
    "/",
    async (AdoptionsDbContext db, CancellationToken cancellationToken) =>
    {
        var results = await db
            .AdoptionApplications.OrderByDescending(a => a.SubmittedAtUtc)
            .Select(a => AdoptionApplicationResponse.From(a))
            .ToListAsync(cancellationToken);

        return Results.Ok(results);
    }
);

// Fetch one application by id. The query filter turns a cross-tenant id into a 404, exactly as
// if the row did not exist — another tenant's application is never distinguishable from a
// missing one.
applications.MapGet(
    "/{id:guid}",
    async (Guid id, AdoptionsDbContext db, CancellationToken cancellationToken) =>
    {
        var application = await db.AdoptionApplications.FirstOrDefaultAsync(
            a => a.Id == id,
            cancellationToken
        );

        return application is null
            ? Results.NotFound()
            : Results.Ok(AdoptionApplicationResponse.From(application));
    }
);

// Record an application in the caller's tenant. Staff enter it on the applicant's behalf —
// there is no public adopter portal (see CHARTER.md's scope boundaries). TenantId comes from
// the resolved ITenantContext (the token), never the request body.
applications.MapPost(
    "/",
    async (
        CreateAdoptionApplicationRequest request,
        AdoptionsDbContext db,
        ITenantContext tenant,
        CancellationToken cancellationToken
    ) =>
    {
        var errors = new Dictionary<string, string[]>();
        if (request.AnimalId == Guid.Empty)
        {
            errors[nameof(request.AnimalId)] = ["An animal is required."];
        }
        if (string.IsNullOrWhiteSpace(request.ApplicantName))
        {
            errors[nameof(request.ApplicantName)] = ["Applicant name is required."];
        }
        if (string.IsNullOrWhiteSpace(request.ApplicantEmail))
        {
            errors[nameof(request.ApplicantEmail)] = ["Applicant email is required."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var application = new AdoptionApplication
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            AnimalId = request.AnimalId,
            ApplicantName = request.ApplicantName,
            ApplicantEmail = request.ApplicantEmail,
            ApplicantPhone = request.ApplicantPhone,
            ApplicantAddress = request.ApplicantAddress,
            Notes = request.Notes,
            Status = AdoptionApplicationStatus.Submitted,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
        };

        db.AdoptionApplications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/applications/{application.Id}",
            AdoptionApplicationResponse.From(application)
        );
    }
);

// Approve an application. The animal's move to Adopted is NOT applied here: this service does
// not own the animal, so it publishes AdoptionApproved and ShelterStack.Animals.Api applies the
// transition. That is asynchronous, so the response returns before the animal has actually
// moved — see AnimalStatusChangeRejectedHandler for what happens when it cannot.
applications.MapPost(
    "/{id:guid}/approve",
    async (
        Guid id,
        HttpContext httpContext,
        AdoptionsDbContext db,
        AnimalLookupClient animals,
        EventPublisher publisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    ) =>
    {
        var application = await db.AdoptionApplications.FirstOrDefaultAsync(
            a => a.Id == id,
            cancellationToken
        );
        if (application is null)
        {
            return Results.NotFound();
        }

        if (!AdoptionApplicationStatusRules.IsDecidable(application.Status))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(application.Status)] =
                    [
                        $"An application that is {application.Status} cannot be approved.",
                    ],
                }
            );
        }

        // Best-effort pre-check, so the common mistake — approving an animal on a medical hold —
        // is a clean 400 here instead of a round trip out to NeedsAttention. It races by nature
        // (the status can change between this lookup and the event being handled); the
        // compensating AnimalStatusChangeRejected path, not this, is the correctness guarantee.
        var lookup = await animals.LookUpAsync(
            application.AnimalId,
            httpContext.Request.Headers.Authorization,
            cancellationToken
        );

        if (lookup.Outcome == AnimalLookupOutcome.NotFound)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(application.AnimalId)] =
                    [
                        "The animal on this application no longer exists in your organisation.",
                    ],
                }
            );
        }

        if (
            lookup.Outcome == AnimalLookupOutcome.Found
            && !AnimalLookupClient.IsAdoptable(lookup.Status!)
        )
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(application.AnimalId)] =
                    [
                        $"The animal is currently {lookup.Status} and cannot be adopted.",
                    ],
                }
            );
        }

        application.Status = AdoptionApplicationStatus.Approved;
        application.StatusReason = null;
        application.DecidedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var logger = loggerFactory.CreateLogger("ShelterStack.Adoptions.Api.Approve");
        try
        {
            await publisher.PublishAsync(
                new AdoptionApproved(
                    application.TenantId,
                    application.Id,
                    application.AnimalId,
                    application.DecidedAtUtc.Value
                ),
                ShelterStackEvents.AdoptionApprovedRoutingKey,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            // The decision is already recorded, so the animal would silently never be adopted.
            // Park the application in NeedsAttention for the same reason the compensating
            // consumer does — a failure staff can see beats one only the logs know about.
            logger.LogError(
                ex,
                "Approved application {ApplicationId} but could not publish '{RoutingKey}'.",
                application.Id,
                ShelterStackEvents.AdoptionApprovedRoutingKey
            );

            application.Status = AdoptionApplicationStatus.NeedsAttention;
            application.StatusReason =
                "The approval could not be sent to the animals service; re-approve once it is reachable.";
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(AdoptionApplicationResponse.From(application));
    }
);

// Reject an application, recording the shelter's reason. Purely local — a rejection changes
// nothing about the animal, so there is no event and no cross-service work.
applications.MapPost(
    "/{id:guid}/reject",
    async (
        Guid id,
        RejectAdoptionApplicationRequest request,
        AdoptionsDbContext db,
        CancellationToken cancellationToken
    ) =>
    {
        var application = await db.AdoptionApplications.FirstOrDefaultAsync(
            a => a.Id == id,
            cancellationToken
        );
        if (application is null)
        {
            return Results.NotFound();
        }

        if (!AdoptionApplicationStatusRules.IsDecidable(application.Status))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(application.Status)] =
                    [
                        $"An application that is {application.Status} cannot be rejected.",
                    ],
                }
            );
        }

        application.Status = AdoptionApplicationStatus.Rejected;
        application.StatusReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason;
        application.DecidedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(AdoptionApplicationResponse.From(application));
    }
);

app.Run();

static async Task SeedDemoTenantsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AdoptionsDbContext>>();

    // Filters apply to queries, not inserts, so a single context (with any tenant)
    // can migrate the schema and seed rows across multiple demo tenants.
    await using var db = new AdoptionsDbContext(options, new StaticTenantContext(Guid.Empty));
    await db.Database.MigrateAsync();

    if (await db.AdoptionApplications.IgnoreQueryFilters().AnyAsync())
    {
        return;
    }

    db.AdoptionApplications.AddRange(DemoAdoptionApplications.All(DateTimeOffset.UtcNow));

    await db.SaveChangesAsync();
}

// Makes the top-level-statement Program class public so the isolation tests' (and any
// future integration tests') WebApplicationFactory<Program> can boot the real DI-wired
// host instead of a hand-rolled stand-in.
public partial class Program;
