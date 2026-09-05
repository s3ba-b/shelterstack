var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var shelterStackDb = postgres.AddDatabase("shelterstackdb");
var identityDb = postgres.AddDatabase("identitydb");
var adoptionsDb = postgres.AddDatabase("adoptionsdb");

var redis = builder.AddRedis("redis").WithDataVolume();

var messaging = builder.AddRabbitMQ("messaging").WithDataVolume();

// Consumes AdoptionApproved from adoptions-api over the broker and publishes
// AnimalStatusChangeRejected back when it cannot apply the move — the first service-to-service
// integration in the codebase.
var animalsApi = builder
    .AddProject<Projects.ShelterStack_Animals_Api>("animals-api")
    .WithReference(shelterStackDb)
    .WaitFor(shelterStackDb)
    .WithReference(messaging)
    .WaitFor(messaging);

var identityApi = builder
    .AddProject<Projects.ShelterStack_Identity_Api>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb);

// Adoption applications (M4). Owns its own adoptionsdb and references animals by id only; it
// pre-checks an animal's status over HTTP before approving, then hands the actual transition to
// animals-api over the broker.
var adoptionsApi = builder
    .AddProject<Projects.ShelterStack_Adoptions_Api>("adoptions-api")
    .WithReference(adoptionsDb)
    .WaitFor(adoptionsDb)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithReference(animalsApi)
    .WaitFor(animalsApi);

var gateway = builder
    .AddProject<Projects.ShelterStack_Gateway>("gateway")
    .WithReference(animalsApi)
    .WaitFor(animalsApi)
    .WithReference(identityApi)
    .WaitFor(identityApi)
    .WithReference(adoptionsApi)
    .WaitFor(adoptionsApi);

// Staff-facing Blazor web app. It talks to the backend only through the gateway
// (never directly to a business service) and is the app's external HTTP endpoint.
var web = builder
    .AddProject<Projects.ShelterStack_Web>("web")
    .WithReference(gateway)
    .WaitFor(gateway)
    .WithExternalHttpEndpoints();

// Dev loop only: run the Tailwind standalone CLI in --watch so edits to the web
// app's markup/tokens regenerate wwwroot/app.css live. Build/CI/publish get their
// CSS from the MSBuild target in ShelterStack.Web.csproj, so this is purely the
// live-reload convenience and is skipped in publish mode. The pinned CLI binary is
// the one that target already cached under the web project's obj/ during build.
if (builder.ExecutionContext.IsRunMode)
{
    var webDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "ShelterStack.Web"));
    var tailwindObj = Path.Combine(webDir, "obj");
    var tailwindCli = Directory.Exists(tailwindObj)
        ? Directory
            .EnumerateFiles(tailwindObj, "tailwindcss-*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "tailwind")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
        : null;

    if (tailwindCli is not null)
    {
        builder
            .AddExecutable(
                "tailwind",
                tailwindCli,
                webDir,
                "-i",
                "Styles/app.tailwind.css",
                "-o",
                "wwwroot/app.css",
                "--watch"
            )
            .WithParentRelationship(web);
    }
}

builder.Build().Run();
