namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// An HTTP service that lives outside the app model (e.g. a SaaS API), which can
/// carry an API key and be replaced by a local mock resource in development.
/// </summary>
public sealed class ExternalHttpServiceResource(string name, Uri url) : Resource(name)
{
    /// <summary>The real service's base address.</summary>
    public Uri Url { get; } = url;

    /// <summary>The API key consumers should send, if the service needs one.</summary>
    public ParameterResource? ApiKey { get; internal set; }

    /// <summary>The resource standing in for the real service, when running as a mock.</summary>
    public IResourceWithEndpoints? Mock { get; internal set; }

    /// <summary>
    /// The environment variable the API key is injected as. It binds to the
    /// <c>{name}:ApiKey</c> configuration key, e.g. <c>unsplash:ApiKey</c>.
    /// </summary>
    public string ApiKeyEnvironmentVariable => $"{Name}__ApiKey";

    // True when WithApiKey() created the parameter, so RunAsMock may swap it
    // for a generated one. A parameter passed in by the caller is never replaced.
    internal bool OwnsApiKeyParameter { get; set; }
}
