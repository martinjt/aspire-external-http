# Aspire: swapping an external service for a local mock

A sample showing how, **with Aspire today**, you swap a real external HTTP API
(Unsplash) for a local mock (a .NET project) depending on the environment.

| Project | What it is |
| --- | --- |
| `ExternalHttp.AppHost` | The Aspire AppHost. Chooses mock vs real Unsplash. |
| `ExternalHttp.Web` | Blazor app that shows a random photo from `unsplash`. |
| `ExternalHttp.UnsplashMock` | Minimal API mimicking `GET /photos/random`. It requires `Authorization: Client-ID <key>` (401 otherwise) and serves generated SVG "photos". |
| `ExternalHttp.ServiceDefaults` | Standard Aspire service defaults. |

Scaffolded with `aspire new aspire-starter` (the template's `ApiService` became `UnsplashMock`).

## How it's wired

The web app only knows two things: a service called `unsplash` (resolved by
service discovery via `https+http://unsplash`) and the `Unsplash:AccessKey`
setting. It has no idea whether it's talking to the mock or to the real API.

The AppHost decides, based on the AppHost environment:

```csharp
if (builder.Environment.IsDevelopment())
{
    var accessKey = builder.AddParameter("unsplash-access-key",
        new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);

    var unsplashMock = builder.AddProject<Projects.ExternalHttp_UnsplashMock>("unsplash")
        .WithHttpHealthCheck("/health")
        .WithEnvironment("Unsplash__AccessKey", accessKey);

    web.WithReference(unsplashMock)
       .WaitFor(unsplashMock)
       .WithEnvironment("Unsplash__AccessKey", accessKey);
}
else
{
    var accessKey = builder.AddParameter("unsplash-access-key", secret: true);
    var unsplash = builder.AddExternalService("unsplash", "https://api.unsplash.com/");

    web.WithReference(unsplash)
       .WithEnvironment("Unsplash__AccessKey", accessKey);
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
| Development | `project.v0` (the mock) | `services__unsplash__http(s)__0={unsplash.bindings.*.url}`, `Unsplash__AccessKey={unsplash-access-key.value}` |
| Production | none (external service) | `services__unsplash__https__0=https://api.unsplash.com/`, `Unsplash__AccessKey={unsplash-access-key.value}` |

## Why this is awkward today

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

The goal of a dedicated integration would be to collapse this to something like
`web.WithReference(unsplash)`, where `unsplash` is a single resource that is the
mock in one environment and the external service in another, carrying its
API key with it.

## Running

```bash
aspire run
```

Open `webfrontend` from the dashboard; each refresh shows a random mock photo.

To see the production model without deploying:

```bash
aspire publish --environment Production
```

> **WSL note:** on WSL, `aspire run` tries to trust the dev certificate in the
> Windows certificate store via `powershell.exe`, which pops a Windows security
> dialog. It sits at "Trusting certificates..." until you accept it on the Windows side.
