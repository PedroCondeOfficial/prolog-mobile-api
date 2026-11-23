using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using prolog_mobile_api.Clients;
using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Tests;

// Boots the real API in memory and replaces only the Prolog client, to check the HTTP contract
// the mobile app sees: routes, JSON shape on success, ProblemDetails with the right status on failure.
public class PrologApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private HttpClient CreateClient(IPrologApiClient prologClient) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing"); // no user secrets, no developer exception page
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrologApi:BaseUrl"] = "https://prolog.test/api",
                ["PrologApi:ApiKey"] = "test-key",
            }));
            b.ConfigureTestServices(s => s.AddSingleton(prologClient));
        }).CreateClient();

    private static HttpClient CreateClient(PrologApiTests t, Func<CancellationToken, Task<IReadOnlyList<PrologProductDto>>> getAll) =>
        t.CreateClient(new StubClient(getAll));

    [Fact]
    public async Task Health_returns_200_when_the_api_is_running()
    {
        var client = CreateClient(this, _ => throw new InvalidOperationException("health must not call the Prolog API"));

        var response = await client.GetAsync("/api/prolog/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("online", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returns_category_summaries_in_the_documented_json_shape()
    {
        var client = CreateClient(this, _ => Task.FromResult<IReadOnlyList<PrologProductDto>>(
        [
            new(1, "Hammer", " TOOLS ", 10m, 2, T0),
            new(2, "Saw", "tools", 5m, 1, T0.AddDays(1)),
            new(3, "Drill", "tools", 50m, 0, T0.AddDays(2)), // out of stock: excluded
            new(4, "Kite", "Toys", 4m, 1, T0),
        ]));

        var response = await client.GetAsync("/api/prolog", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, categories.GetArrayLength());

        var tools = categories[0];
        Assert.Equal("Tools", tools.GetProperty("category").GetString());
        Assert.Equal(25m, tools.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(2, tools.GetProperty("availableRecordCount").GetInt32());

        var mostRecent = tools.GetProperty("mostRecentlyUpdated");
        Assert.Equal(2, mostRecent.GetProperty("id").GetInt32());
        Assert.Equal("Saw", mostRecent.GetProperty("name").GetString());
        Assert.Equal(T0.AddDays(1), mostRecent.GetProperty("lastUpdated").GetDateTimeOffset());

        var items = tools.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("Tools", items[0].GetProperty("category").GetString());
    }

    [Theory]
    [InlineData(PrologApiFailure.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(PrologApiFailure.CircuitOpen, HttpStatusCode.ServiceUnavailable)]
    [InlineData(PrologApiFailure.RateLimited, HttpStatusCode.ServiceUnavailable)]
    [InlineData(PrologApiFailure.Unreachable, HttpStatusCode.BadGateway)]
    [InlineData(PrologApiFailure.Unauthorized, HttpStatusCode.BadGateway)]
    [InlineData(PrologApiFailure.ErrorResponse, HttpStatusCode.BadGateway)]
    [InlineData(PrologApiFailure.InvalidResponse, HttpStatusCode.BadGateway)]
    public async Task Prolog_api_failures_become_problem_details(PrologApiFailure failure, HttpStatusCode expected)
    {
        var client = CreateClient(this, _ => throw new PrologApiException(failure, "internal detail that must not leak"));

        var response = await client.GetAsync("/api/prolog", Ct);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("internal detail that must not leak", raw);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(raw, JsonSerializerOptions.Web);
        Assert.Equal((int)expected, problem!.Status);
        Assert.Equal(failure.ToString(), problem.Extensions["reason"]?.ToString());
    }

    [Fact]
    public async Task Unexpected_exceptions_become_a_generic_500_without_a_stack_trace()
    {
        var client = CreateClient(this, _ => throw new InvalidOperationException("secret internals"));

        var response = await client.GetAsync("/api/prolog", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("secret internals", raw);
        Assert.DoesNotContain("prolog_mobile_api.", raw);
    }

    private sealed class StubClient(Func<CancellationToken, Task<IReadOnlyList<PrologProductDto>>> getAll) : IPrologApiClient
    {
        public Task<IReadOnlyList<PrologProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default) =>
            getAll(cancellationToken);
    }
}
