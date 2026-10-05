# Aspire: swapping an external service for a local mock

A sample showing how to swap a real external HTTP API (Unsplash) for a local
mock (a .NET project) depending on the environment, done two ways:

1. [**Today**](#today-an-if-in-the-apphost): plain Aspire, with an `if` in the AppHost.
2. [**Proposed**](#proposed-addexternalhttpservice--runasmock): an `AddExternalHttpService().WithApiKey().RunAsMock(...)` integration, with no `if`.

Both AppHosts run the same web app and mock, unchanged.

| Project | What it is |
| --- | --- |
| `ExternalHttp.AppHost` | AppHost using today's `if`/`else` approach. |
| `ExternalHttp.IntegrationAppHost` | AppHost using the proposed integration. |
| `ExternalHttp.Hosting` | The proposed integration (`AddExternalHttpService`, `WithApiKey`, `RunAsMock`, `WithReference`). |
| `ExternalHttp.Web` | Blazor app that shows a random photo from `unsplash`. |
| `ExternalHttp.UnsplashMock` | Minimal API mimicking `GET /photos/random`. It requires `Authorization: Client-ID <key>` (401 otherwise) and serves generated SVG "photos". |
| `ExternalHttp.ServiceDefaults` | Standard Aspire service defaults. |

Scaffolded with `aspire new aspire-starter` (the template's `ApiService` became
`UnsplashMock`) and `dotnet new aspire-apphost` for the second AppHost.

The web app only knows two things: a service called `unsplash` (resolved by
service discovery via `https+http://unsplash`) and the `Unsplash:ApiKey`
setting. It has no idea whether it's talking to the mock or to the real API.

## Today: an `if` in the AppHost

`ExternalHttp.AppHost/AppHost.cs` decides, based on the AppHost environment:

```csharp
if (builder.Environment.IsDevelopment())
{
    var accessKey = builder.AddParameter("unsplash-access-key",
        new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);

    var unsplashMock = builder.AddProject<Projects.ExternalHttp_UnsplashMock>("unsplash")
        .WithHttpHealthCheck("/health")
        .WithEnvironment("Unsplash__ApiKey", accessKey);

    web.WithReference(unsplashMock)
       .WaitFor(unsplashMock)
       .WithEnvironment("Unsplash__ApiKey", accessKey);
}
else
{
    var accessKey = builder.AddParameter("unsplash-access-key", secret: true);
    var unsplash = builder.AddExternalService("unsplash", "https://api.unsplash.com/");

    web.WithReference(unsplash)
       .WithEnvironment("Unsplash__ApiKey", accessKey);
}
```

* `aspire run`: Development, so the mock is used. A random key is generated
  and handed to both the mock and the web app, so no one needs a real Unsplash key.
* `aspire publish` / `aspire deploy`: Production by default, so the real
  `https://api.unsplash.com/` is used and `unsplash-access-key` must be supplied.
* `aspire deploy --environment Development`: deploys the mock alongside the app.

Result (from the generated manifest):

| Environment | `unsplash` resource | Injected into `webfrontend` |
| --- | --- | --- |
| Development | `project.v0` (the mock) | `services__unsplash__http(s)__0={unsplash.bindings.*.url}`, `Unsplash__ApiKey={unsplash-access-key.value}` |
| Production | none (external service) | `services__unsplash__https__0=https://api.unsplash.com/`, `Unsplash__ApiKey={unsplash-access-key.value}` |

### Why this is awkward today

1. **No common type to reference.** `AddExternalService` returns
   `IResourceBuilder<ExternalServiceResource>`; `AddProject` returns
   `IResourceBuilder<ProjectResource>`. `WithReference` has separate overloads
   for each, so you can't write
   `var unsplash = isDev ? mock : external; web.WithReference(unsplash);`.
   The `if` has to wrap the consumer wiring, not just the resource choice.
2. **Every consumer is duplicated per branch.** Each additional app that
   needs Unsplash has to be wired into both branches, too.
3. **The "credentials" story differs per branch.** The mock needs the key
   pushed into it, the real service doesn't, and dev wants a generated key while
   prod wants a required secret. That's two different parameter definitions with the same name.
4. **Lifecycle differs.** `WaitFor` and health checks make sense for the mock
   but not for the external service, which adds more per-branch code.

## Proposed: `AddExternalHttpService` + `RunAsMock`

`ExternalHttp.IntegrationAppHost/AppHost.cs` builds the same app with the
integration in `ExternalHttp.Hosting`:

```csharp
var unsplash = builder.AddExternalHttpService("unsplash", "https://api.unsplash.com/")
    .WithApiKey()
    .RunAsMock(builder.AddProject<Projects.ExternalHttp_UnsplashMock>("unsplash-mock")
        .WithHttpHealthCheck("/health"));

builder.AddProject<Projects.ExternalHttp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(unsplash);
```

`unsplash` is a single `ExternalHttpServiceResource` exposing `Url`, `ApiKey`
(a `ParameterResource`) and `Mock`. Every consumer gets one `.WithReference(unsplash)`,
whatever the environment.

| API | Behaviour |
| --- | --- |
| `AddExternalHttpService(name, url)` | Adds the service. Shown in the dashboard; never deployed itself. |
| `WithApiKey()` | Adds a secret `{name}-api-key` parameter. Required outside development; **generated** when the service runs as a mock, so developers never need a real key. |
| `WithApiKey(parameter)` | Uses your parameter in every environment. |
| `RunAsMock(project or container)` | **Development only:** routes the service to the mock's `http`/`https` endpoints, injects the API key into the mock (so it can enforce it), and parents the mock under the service in the dashboard. **Any other environment:** removes the mock from the model, so it's never deployed. |
| `.WithReference(service)` | Injects `services__{name}__{scheme}__0` (the mock's endpoints, or the real URL) and `{name}__ApiKey`. Waits for the mock when there is one. |

Result (from the generated manifest):

| Environment | Resources | Injected into `webfrontend` |
| --- | --- | --- |
| Development | `unsplash-mock` (project), `unsplash-api-key` (generated secret) | `services__unsplash__http(s)__0={unsplash-mock.bindings.*.url}`, `unsplash__ApiKey={unsplash-api-key.value}` |
| Production | `unsplash-api-key` (required secret) | `services__unsplash__https__0=https://api.unsplash.com/`, `unsplash__ApiKey={unsplash-api-key.value}` |

Notes:

* Call `RunAsMock` before passing the service to `WithReference`. The
  environment variables are resolved lazily, but `WaitFor` on the mock is wired eagerly.
* `WithApiKey()` and `RunAsMock` can come in either order.
* The mock is still declared in the AppHost (so it gets `Projects.*` and normal
  configuration), but it only exists in the running model in development.

## Running

```bash
aspire run                     # today's approach
aspire run --apphost ExternalHttp.IntegrationAppHost/ExternalHttp.IntegrationAppHost.csproj   # proposed integration
```

Open `webfrontend` from the dashboard; each refresh shows a random mock photo.

To see the production model without deploying:

```bash
aspire publish --environment Production
aspire publish --environment Production --apphost ExternalHttp.IntegrationAppHost/ExternalHttp.IntegrationAppHost.csproj
```

> **WSL note:** on WSL, `aspire run` tries to trust the dev certificate in the
> Windows certificate store via `powershell.exe`, which pops a Windows security
> dialog. It sits at "Trusting certificates..." until you accept it on the Windows side.
