using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace prolog_mobile_api.Tests;

// Stands in for the network at the very bottom of the HttpClient pipeline, so everything
// above it (retries, timeouts, our error translation) runs for real.
public sealed class FakeHttpHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private int _calls;
    private int _inFlight;
    private int _maxInFlight;

    public int Calls => _calls;
    public int MaxInFlight => _maxInFlight;
    public ConcurrentQueue<Uri> Requests { get; } = new();
    public ConcurrentQueue<string?> ApiKeys { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Enqueue(request.RequestUri!);
        ApiKeys.Enqueue(request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null);
        var attempt = Interlocked.Increment(ref _calls);

        var inFlight = Interlocked.Increment(ref _inFlight);
        InterlockedMax(ref _maxInFlight, inFlight);
        try
        {
            return await respond(request, attempt, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = target) && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static int PageNumber(HttpRequestMessage request) =>
        int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!);

    // Simulates the Prolog API's paging: honours ?page and ?pageSize over `totalRecords` generated records.
    public static HttpResponseMessage Page(HttpRequestMessage request, int totalRecords)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var page = int.Parse(query["page"]!);
        var pageSize = int.Parse(query["pageSize"]!);
        var skip = (page - 1) * pageSize;

        var records = Enumerable.Range(skip, Math.Max(0, Math.Min(pageSize, totalRecords - skip)))
            .Select(i => new
            {
                id = 1000 + i,
                name = $"Item {1000 + i}",
                category = "  toys ",
                price = 2.50m,
                quantity = i % 3,
                lastUpdated = "2026-09-01T00:00:00Z",
            });

        return Json(JsonSerializer.Serialize(new
        {
            records,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
            totalRecords,
        }));
    }
}
