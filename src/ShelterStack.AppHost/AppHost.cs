var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var shelterStackDb = postgres.AddDatabase("shelterstackdb");
var identityDb = postgres.AddDatabase("identitydb");

var redis = builder.AddRedis("redis").WithDataVolume();

var messaging = builder.AddRabbitMQ("messaging").WithDataVolume();

var animalsApi = builder
    .AddProject<Projects.ShelterStack_Animals_Api>("animals-api")
    .WithReference(shelterStackDb)
    .WaitFor(shelterStackDb);

var identityApi = builder
    .AddProject<Projects.ShelterStack_Identity_Api>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb);

var gateway = builder
    .AddProject<Projects.ShelterStack_Gateway>("gateway")
    .WithReference(animalsApi)
    .WaitFor(animalsApi)
    .WithReference(identityApi)
    .WaitFor(identityApi);

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
