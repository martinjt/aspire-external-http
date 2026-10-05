// The same app as ExternalHttp.AppHost, using the proposed integration instead
// of an if/else. The environment decision lives inside RunAsMock:
//
// * Development: "unsplash" routes to the mock project, and the API key is
//   generated and handed to both the mock and every consumer.
// * Anything else: "unsplash" is https://api.unsplash.com/, the mock is removed
//   from the model, and "unsplash-api-key" must be supplied as a secret.

var builder = DistributedApplication.CreateBuilder(args);

var unsplash = builder.AddExternalHttpService("unsplash", "https://api.unsplash.com/")
    .WithApiKey()
    .RunAsMock(builder.AddProject<Projects.ExternalHttp_UnsplashMock>("unsplash-mock")
        .WithHttpHealthCheck("/health"));

builder.AddProject<Projects.ExternalHttp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(unsplash);

builder.Build().Run();
