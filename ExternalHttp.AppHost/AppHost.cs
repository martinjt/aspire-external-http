using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var web = builder.AddProject<Projects.ExternalHttp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// ---------------------------------------------------------------------------
// What you have to do TODAY to swap an external service for a local mock.
//
// `AddExternalService` returns IResourceBuilder<ExternalServiceResource> and
// `AddProject` returns IResourceBuilder<ProjectResource>. They share no common
// type that `WithReference` accepts, so you can't pick one in a ternary and
// make a single `.WithReference(unsplash)` call. The whole wiring (resource,
// API key, reference, WaitFor) ends up duplicated in each branch.
//
// The switch is the AppHost environment rather than run/publish mode, so the
// same model covers `aspire run` (Development by default) and a dev
// deployment (`aspire deploy --environment Development`). Publish/deploy
// default to Production, which gets the real Unsplash API.
// ---------------------------------------------------------------------------
if (builder.Environment.IsDevelopment())
{
    // The mock enforces an API key just like the real thing, but nobody needs
    // a real Unsplash key: Aspire generates one and hands it to both sides.
    var accessKey = builder.AddParameter(
        "unsplash-access-key",
        new GenerateParameterDefault { MinLength = 32, Special = false },
        secret: true);

    var unsplashMock = builder.AddProject<Projects.ExternalHttp_UnsplashMock>("unsplash")
        .WithHttpHealthCheck("/health")
        .WithEnvironment("Unsplash__ApiKey", accessKey);

    web.WithReference(unsplashMock)
       .WaitFor(unsplashMock)
       .WithEnvironment("Unsplash__ApiKey", accessKey);
}
else
{
    var unsplash = builder.AddExternalService("unsplash", "https://api.unsplash.com/");

    web.WithReference(unsplash);

    // SPIKE: WithApiKey as an extension of the built-in ExternalServiceResource.
    // Adds a required "unsplash-api-key" secret and injects it as unsplash__ApiKey
    // into every resource that references the service (here, after the fact).
    unsplash.WithApiKey();
}

builder.Build().Run();
