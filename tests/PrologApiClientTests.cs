using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using prolog_mobile_api.Clients;

namespace prolog_mobile_api.Tests;

// Builds the client through the same AddPrologApiClient registration the app uses, with only
// the network swapped out, so these test the real resilience pipeline, not a copy of it.
public class PrologApiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IPrologApiClient CreateClient(FakeHttpHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrologApi:BaseUrl"] = "https://prolog.test/api",
                ["PrologApi:ApiKey"] = "test-key",
                // Same policy as production, but no backoff delay and short timeouts so tests are fast.
                ["PrologApi:Resilience:Retry:Delay"] = "00:00:00",
                ["PrologApi:Resilience:AttemptTimeout:Timeout"] = "00:00:00.200",
                ["PrologApi:Resilience:TotalRequestTimeout:Timeout"] = "00:00:05",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrologApiClient(config).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IPrologApiClient>();
    }

    // ---- Paging and concurrency ----

    [Fact]
    public async Task Fetches_every_page_and_returns_all_records()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(FakeHttpHandler.Page(req, totalRecords: 60)));

        var items = await CreateClient(handler).GetAllProductsAsync(Ct);

        Assert.Equal(60, items.Count);
        Assert.Equal(Enumerable.Range(1000, 60), items.Select(i => i.Id)); // in page order
        Assert.Equal(3, handler.Calls);
        Assert.Equal([1, 2, 3], handler.Requests.Select(u => int.Parse(System.Web.HttpUtility.ParseQueryString(u.Query)["page"]!)).Order());
        Assert.All(handler.Requests, u => Assert.Contains("pageSize=25", u.Query));
    }

    [Fact]
    public async Task Calls_the_records_endpoint_under_the_base_url_with_the_api_key_header()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(FakeHttpHandler.Page(req, totalRecords: 1)));

        await CreateClient(handler).GetAllProductsAsync(Ct);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("/api/records", uri.AbsolutePath);
        Assert.Equal("test-key", Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task Single_page_result_makes_only_one_request()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(FakeHttpHandler.Page(req, totalRecords: 10)));

        var items = await CreateClient(handler).GetAllProductsAsync(Ct);

        Assert.Equal(10, items.Count);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Remaining_pages_are_fetched_concurrently_but_never_more_than_four_at_once()
    {
        var handler = new FakeHttpHandler(async (req, _, ct) =>
        {
            if (FakeHttpHandler.PageNumber(req) > 1)
            {
                await Task.Delay(100, ct); // hold each request open so overlapping ones are observable
            }
            return FakeHttpHandler.Page(req, totalRecords: 25 * 9);
        });

        var items = await CreateClient(handler).GetAllProductsAsync(Ct);

        Assert.Equal(25 * 9, items.Count);
        Assert.Equal(4, handler.MaxInFlight);
    }

    [Fact]
    public async Task Records_duplicated_across_pages_are_returned_once()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(FakeHttpHandler.Json($$"""
            {
              "records": [{ "id": 1, "name": "Same", "category": "toys", "price": 1, "quantity": 1, "lastUpdated": "2026-09-01T00:00:00Z" }],
              "page": {{FakeHttpHandler.PageNumber(req)}}, "pageSize": 25, "totalPages": 2, "totalRecords": 2
            }
            """)));

        var items = await CreateClient(handler).GetAllProductsAsync(Ct);

        Assert.Single(items);
    }

    [Fact]
    public async Task Maps_every_field_from_the_api_response()
    {
        var handler = new FakeHttpHandler((_, _, _) => Task.FromResult(FakeHttpHandler.Json("""
            {
              "records": [{ "id": 1001, "name": "Thermal Receipt Printer", "category": "Electronics",
                            "price": 4.99, "quantity": 7, "lastUpdated": "2026-09-09T22:31:00Z" }],
              "page": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1
            }
            """)));

        var item = Assert.Single(await CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(1001, item.Id);
        Assert.Equal("Thermal Receipt Printer", item.Name);
        Assert.Equal("Electronics", item.Category);
        Assert.Equal(4.99m, item.Price);
        Assert.Equal(7, item.Quantity);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 22, 31, 0, TimeSpan.Zero), item.LastUpdated);
    }

    // ---- Resilience and error translation ----

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Transient_failure_is_retried_and_then_succeeds(HttpStatusCode transient)
    {
        var handler = new FakeHttpHandler((req, attempt, _) => Task.FromResult(
            attempt == 1 ? FakeHttpHandler.Status(transient) : FakeHttpHandler.Page(req, totalRecords: 3)));

        var items = await CreateClient(handler).GetAllProductsAsync(Ct);

        Assert.Equal(3, items.Count);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, PrologApiFailure.ErrorResponse)]
    [InlineData(HttpStatusCode.TooManyRequests, PrologApiFailure.RateLimited)]
    public async Task Persistent_transient_failure_exhausts_retries(HttpStatusCode status, PrologApiFailure expected)
    {
        var handler = new FakeHttpHandler((_, _, _) => Task.FromResult(FakeHttpHandler.Status(status)));

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(expected, ex.Failure);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.Equal(4, handler.Calls); // 1 attempt + 3 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PrologApiFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PrologApiFailure.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, PrologApiFailure.ErrorResponse)]
    [InlineData(HttpStatusCode.BadRequest, PrologApiFailure.ErrorResponse)]
    public async Task Non_transient_4xx_is_not_retried(HttpStatusCode status, PrologApiFailure expected)
    {
        var handler = new FakeHttpHandler((_, _, _) => Task.FromResult(FakeHttpHandler.Status(status)));

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(expected, ex.Failure);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transport_error_is_retried_then_reported_as_unreachable()
    {
        var handler = new FakeHttpHandler((_, _, _) => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(PrologApiFailure.Unreachable, ex.Failure);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task Hung_api_times_out_instead_of_waiting_forever()
    {
        var handler = new FakeHttpHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct); // never responds; only the timeout's cancellation ends it
            throw new InvalidOperationException("unreachable");
        });

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(PrologApiFailure.Timeout, ex.Failure);
        Assert.Equal(4, handler.Calls); // each attempt timed out and was retried
    }

    [Fact]
    public async Task Malformed_json_is_reported_as_invalid_response_without_retrying()
    {
        var handler = new FakeHttpHandler((_, _, _) => Task.FromResult(FakeHttpHandler.Json("<html>oops</html>")));

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(PrologApiFailure.InvalidResponse, ex.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task One_failing_page_fails_the_whole_call_rather_than_returning_partial_data()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(
            FakeHttpHandler.PageNumber(req) == 3
                ? FakeHttpHandler.Status(HttpStatusCode.BadRequest)
                : FakeHttpHandler.Page(req, totalRecords: 100)));

        var ex = await Assert.ThrowsAsync<PrologApiException>(() => CreateClient(handler).GetAllProductsAsync(Ct));

        Assert.Equal(PrologApiFailure.ErrorResponse, ex.Failure);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_disguised_as_an_api_failure()
    {
        var handler = new FakeHttpHandler((req, _, _) => Task.FromResult(FakeHttpHandler.Page(req, totalRecords: 1)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).GetAllProductsAsync(cts.Token));

        Assert.IsNotType<PrologApiException>(ex);
    }
}
