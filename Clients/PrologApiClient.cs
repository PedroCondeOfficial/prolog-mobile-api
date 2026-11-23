using System.Net;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Clients;

public interface IPrologApiClient
{
    /// <summary>Fetches every product record, walking all pages of the Prolog API.</summary>
    Task<IReadOnlyList<PrologProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default);
}

// Retries, timeouts and the circuit breaker live in the HttpClient pipeline (see Program.cs).
// This class handles paging and translating whatever is left into PrologApiException.
public class PrologApiClient(HttpClient httpClient, ILogger<PrologApiClient> logger) : IPrologApiClient
{
    public const string ApiKeyHeaderName = "X-Api-Key";
    private const string RecordsPath = "records";

    // API allows pageSize 1-25; the largest page means the fewest requests.
    private const int PageSize = 25;

    // Upper bound on in-flight requests, so we don't hammer the API into rate-limiting us.
    private const int MaxConcurrentRequests = 4;

    public async Task<IReadOnlyList<PrologProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default)
    {
        // The first page tells us how many pages there are; the rest are fetched concurrently.
        var firstPage = await GetPageAsync(1, cancellationToken);

        var totalPages = Math.Max(1, firstPage.TotalPages);
        var pages = new PrologRecordsPageDto[totalPages];
        pages[0] = firstPage;

        // If any page fails, the whole call fails: partial data would produce wrong totals.
        await Parallel.ForEachAsync(
            Enumerable.Range(2, totalPages - 1),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests, CancellationToken = cancellationToken },
            async (page, token) => pages[page - 1] = await GetPageAsync(page, token));

        return pages
            .SelectMany(p => p.Records)
            .DistinctBy(r => r.Id) // records can shift between pages while we're paging
            .ToList();
    }

    private async Task<PrologRecordsPageDto> GetPageAsync(int page, CancellationToken cancellationToken)
    {
        var path = $"{RecordsPath}?page={page}&pageSize={PageSize}";
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new PrologApiException(PrologApiFailure.Unauthorized,
                    "Prolog API rejected the credentials.", (int)response.StatusCode);
            }
            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                throw new PrologApiException(PrologApiFailure.RateLimited,
                    "Prolog API rate limit still exceeded after retrying.", (int)response.StatusCode);
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new PrologApiException(PrologApiFailure.ErrorResponse,
                    $"Prolog API returned {(int)response.StatusCode}.", (int)response.StatusCode);
            }

            return await response.Content.ReadFromJsonAsync<PrologRecordsPageDto>(cancellationToken)
                ?? throw new PrologApiException(PrologApiFailure.InvalidResponse, "Prolog API returned an empty response body.");
        }
        catch (PrologApiException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // our caller cancelled (e.g. the app disconnected): not a Prolog API failure
        }
        catch (Exception ex) when (Classify(ex) is { } failure)
        {
            logger.LogWarning(ex, "Prolog API request {Path} failed: {Failure}", path, failure);
            throw new PrologApiException(failure, $"Prolog API call failed: {failure}.", inner: ex);
        }
    }

    private static PrologApiFailure? Classify(Exception ex) => ex switch
    {
        TimeoutRejectedException => PrologApiFailure.Timeout,   // resilience attempt/total timeout
        TaskCanceledException => PrologApiFailure.Timeout,      // HttpClient.Timeout
        BrokenCircuitException => PrologApiFailure.CircuitOpen,
        HttpRequestException => PrologApiFailure.Unreachable,
        JsonException or NotSupportedException => PrologApiFailure.InvalidResponse, // bad JSON / wrong content type
        _ => null, // genuinely unexpected: let it surface as a 500 rather than mislabel it
    };
}
