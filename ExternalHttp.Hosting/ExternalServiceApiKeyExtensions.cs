using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>Marks an <see cref="ExternalServiceResource"/> as requiring an API key.</summary>
public sealed class ExternalServiceApiKeyAnnotation(ParameterResource apiKey) : IResourceAnnotation
{
    public ParameterResource ApiKey { get; } = apiKey;
}

/// <summary>
/// Adds API key support to the built-in <c>AddExternalService</c> resource.
/// </summary>
/// <remarks>
/// <see cref="ExternalServiceResource"/> is sealed and the built-in
/// <c>WithReference</c> has no extension point, so the key is stored as an
/// annotation and pushed into consumers from a <see cref="BeforeStartEvent"/>
/// handler, which runs in both run and publish mode, after the model is built
/// but before any environment callbacks are evaluated.
/// </remarks>
public static class ExternalServiceApiKeyExtensions
{
    // The relationship type WithReference/WithReferenceRelationship records;
    // Aspire's KnownRelationshipTypes is internal.
    private const string ReferenceRelationshipType = "Reference";

    extension(ExternalServiceResource resource)
    {
        /// <summary>The API key consumers should send, if one was configured with <c>WithApiKey</c>.</summary>
        public ParameterResource? ApiKey =>
            resource.Annotations.OfType<ExternalServiceApiKeyAnnotation>().LastOrDefault()?.ApiKey;

        /// <summary>
        /// The environment variable the API key is injected as. It binds to the
        /// <c>{name}:ApiKey</c> configuration key, e.g. <c>unsplash:ApiKey</c>.
        /// </summary>
        public string ApiKeyEnvironmentVariable => $"{resource.Name}__ApiKey";
    }

    /// <summary>
    /// Requires an API key for the external service, stored in a secret
    /// parameter named <c>{name}-api-key</c>. Every resource that references the
    /// service with <c>WithReference</c> receives it as <c>{name}__ApiKey</c>.
    /// </summary>
    public static IResourceBuilder<ExternalServiceResource> WithApiKey(
        this IResourceBuilder<ExternalServiceResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var apiKey = builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-api-key", secret: true);
        return builder.WithApiKey(apiKey);
    }

    /// <summary>Uses <paramref name="apiKey"/> as the external service's API key.</summary>
    public static IResourceBuilder<ExternalServiceResource> WithApiKey(
        this IResourceBuilder<ExternalServiceResource> builder, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);

        var service = builder.Resource;
        var subscribe = service.ApiKey is null;

        builder.WithAnnotation(new ExternalServiceApiKeyAnnotation(apiKey.Resource), ResourceAnnotationMutationBehavior.Replace);

        // Consumers may call WithReference before or after WithApiKey, so they
        // are only discovered once the model is complete.
        if (subscribe)
        {
            builder.ApplicationBuilder.Eventing.Subscribe<BeforeStartEvent>((e, _) =>
            {
                foreach (var consumer in e.Model.Resources.OfType<IResourceWithEnvironment>())
                {
                    var references = consumer.Annotations.OfType<ResourceRelationshipAnnotation>()
                        .Any(r => r.Type == ReferenceRelationshipType && ReferenceEquals(r.Resource, service));

                    if (references)
                    {
                        consumer.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                        {
                            if (service.ApiKey is { } key)
                            {
                                context.EnvironmentVariables[service.ApiKeyEnvironmentVariable] = key;
                            }
                        }));
                    }
                }

                return Task.CompletedTask;
            });
        }

        return builder;
    }
}
