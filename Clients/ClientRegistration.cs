using Microsoft.Extensions.Options;
using prolog_mobile_api.Config;

namespace prolog_mobile_api.Clients;

public static class ClientRegistration
{
    // One place that wires the Prolog client, so the app and the tests use the exact same
    // pipeline. Returns the builder so tests can swap the network handler underneath it.
    public static IHttpClientBuilder AddPrologApiClient(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PrologApiOptions>()
            .Bind(config.GetSection(PrologApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var builder = services.AddHttpClient<IPrologApiClient, PrologApiClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<PrologApiOptions>>().Value;
            // Trailing slash so relative paths append to the base URL instead of replacing its last segment.
            http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Add(PrologApiClient.ApiKeyHeaderName, options.ApiKey);
        });

        // Standard pipeline: total timeout -> retry (exponential backoff + jitter, honours Retry-After)
        // -> circuit breaker -> per-attempt timeout. Retries 429, 408, 5xx and transport errors; not other 4xx.
        // Tunable via "PrologApi:Resilience" in appsettings.json.
        builder.AddStandardResilienceHandler(config.GetSection($"{PrologApiOptions.SectionName}:Resilience"));

        return builder;
    }
}
