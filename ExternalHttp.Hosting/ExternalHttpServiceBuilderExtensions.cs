using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

namespace Aspire.Hosting;

public static class ExternalHttpServiceBuilderExtensions
{
    /// <summary>
    /// Adds an external HTTP service. Consumers resolve it by <paramref name="name"/>
    /// through service discovery, e.g. <c>https+http://{name}</c>.
    /// </summary>
    public static IResourceBuilder<ExternalHttpServiceResource> AddExternalHttpService(
        this IDistributedApplicationBuilder builder, [ResourceName] string name, string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return builder.AddExternalHttpService(name, new Uri(url, UriKind.Absolute));
    }

    /// <inheritdoc cref="AddExternalHttpService(IDistributedApplicationBuilder, string, string)"/>
    public static IResourceBuilder<ExternalHttpServiceResource> AddExternalHttpService(
        this IDistributedApplicationBuilder builder, [ResourceName] string name, Uri url)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The URL must be an absolute http or https address.", nameof(url));
        }

        // Only consumers' environment variables reach the manifest; the service
        // itself is not something to deploy.
        return builder.AddResource(new ExternalHttpServiceResource(name, url))
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "ExternalHttpService",
                State = new ResourceStateSnapshot("External", KnownResourceStateStyles.Info),
                Properties = [new(CustomResourceKnownProperties.Source, url.ToString())],
            })
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Requires an API key for the service, stored in a secret parameter named
    /// <c>{name}-api-key</c>. When the service runs as a mock, the key is
    /// generated instead, so developers don't need a real one.
    /// </summary>
    public static IResourceBuilder<ExternalHttpServiceResource> WithApiKey(
        this IResourceBuilder<ExternalHttpServiceResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resource = builder.Resource;
        if (resource.ApiKey is not null)
        {
            throw new InvalidOperationException($"'{resource.Name}' already has an API key.");
        }

        resource.ApiKey = resource.Mock is null
            ? AddRequiredApiKey(builder.ApplicationBuilder, resource)
            : AddGeneratedApiKey(builder.ApplicationBuilder, resource);
        resource.OwnsApiKeyParameter = true;

        return builder;
    }

    /// <summary>Uses <paramref name="apiKey"/> as the service's API key, in every environment.</summary>
    public static IResourceBuilder<ExternalHttpServiceResource> WithApiKey(
        this IResourceBuilder<ExternalHttpServiceResource> builder, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);

        if (builder.Resource.ApiKey is not null)
        {
            throw new InvalidOperationException($"'{builder.Resource.Name}' already has an API key.");
        }

        builder.Resource.ApiKey = apiKey.Resource;
        builder.Resource.OwnsApiKeyParameter = false;

        return builder;
    }

    /// <summary>
    /// In development, routes the service to <paramref name="mock"/> instead of the
    /// real URL and hands the mock the API key so it can enforce it. In any other
    /// environment the mock is removed from the app model, so it is never deployed.
    /// </summary>
    public static IResourceBuilder<ExternalHttpServiceResource> RunAsMock<TMock>(
        this IResourceBuilder<ExternalHttpServiceResource> builder, IResourceBuilder<TMock> mock)
        where TMock : IResourceWithEndpoints, IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(mock);

        var app = builder.ApplicationBuilder;
        var resource = builder.Resource;

        if (!app.Environment.IsDevelopment())
        {
            app.Resources.Remove(mock.Resource);
            return builder;
        }

        if (resource.Mock is not null)
        {
            throw new InvalidOperationException($"'{resource.Name}' already runs as mock '{resource.Mock.Name}'.");
        }

        resource.Mock = mock.Resource;

        // A key WithApiKey() asked the user for isn't needed against the mock.
        if (resource.OwnsApiKeyParameter && resource.ApiKey is { } required)
        {
            app.Resources.Remove(required);
            resource.ApiKey = AddGeneratedApiKey(app, resource);
        }

        mock.WithParentRelationship(resource)
            .WithEnvironment(context =>
            {
                if (resource.ApiKey is { } apiKey)
                {
                    context.EnvironmentVariables[resource.ApiKeyEnvironmentVariable] = apiKey;
                }
            });

        foreach (var snapshot in resource.Annotations.OfType<ResourceSnapshotAnnotation>().ToList())
        {
            resource.Annotations.Remove(snapshot);
        }

        builder.WithInitialState(new CustomResourceSnapshot
        {
            ResourceType = "ExternalHttpService",
            State = new ResourceStateSnapshot($"Mocked by {mock.Resource.Name}", KnownResourceStateStyles.Info),
            Properties = [new(CustomResourceKnownProperties.Source, resource.Url.ToString())],
        });

        return builder;
    }

    /// <summary>
    /// Injects the service's address (the mock's endpoints in development, the
    /// real URL otherwise) as service discovery configuration under the service's
    /// name, plus its API key as <c>{name}__ApiKey</c>.
    /// </summary>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<ExternalHttpServiceResource> service)
        where TDestination : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(service);

        var resource = service.Resource;

        builder.WithReferenceRelationship(resource)
            .WithEnvironment(context =>
            {
                if (resource.Mock is { } mock)
                {
                    foreach (var endpoint in mock.GetEndpoints())
                    {
                        if (endpoint.Scheme is "http" or "https")
                        {
                            context.EnvironmentVariables[$"services__{resource.Name}__{endpoint.EndpointName}__0"] = endpoint;
                        }
                    }
                }
                else
                {
                    context.EnvironmentVariables[$"services__{resource.Name}__{resource.Url.Scheme}__0"] = resource.Url.ToString();
                }

                if (resource.ApiKey is { } apiKey)
                {
                    context.EnvironmentVariables[resource.ApiKeyEnvironmentVariable] = apiKey;
                }
            });

        // RunAsMock has to come before WithReference for the wait to be wired.
        if (resource.Mock is { } mock)
        {
            builder.WaitFor(builder.ApplicationBuilder.CreateResourceBuilder(mock));
        }

        return builder;
    }

    private static ParameterResource AddRequiredApiKey(IDistributedApplicationBuilder app, ExternalHttpServiceResource resource) =>
        app.AddParameter($"{resource.Name}-api-key", secret: true).Resource;

    private static ParameterResource AddGeneratedApiKey(IDistributedApplicationBuilder app, ExternalHttpServiceResource resource) =>
        app.AddParameter(
            $"{resource.Name}-api-key",
            new GenerateParameterDefault { MinLength = 32, Special = false },
            secret: true).Resource;
}
