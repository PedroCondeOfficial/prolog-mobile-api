using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Clients;

public interface IPrologApiClient
{
    /// <summary>Fetches every product record, walking all pages of the Prolog API.</summary>
    Task<IReadOnlyList<PrologProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default);
}

public class PrologApiClient(HttpClient httpClient) : IPrologApiClient
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
        // TODO: error handling (non-success status codes, timeouts, retries).
        var result = await httpClient.GetFromJsonAsync<PrologRecordsPageDto>(
            $"{RecordsPath}?page={page}&pageSize={PageSize}", cancellationToken);

        return result ?? throw new InvalidOperationException("Prolog API returned an empty response body.");
    }
}
