using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Clients;

public interface IPrologApiClient
{
    /// <summary>Fetches a single page of products.</summary>
    Task<PrologRecordsPageDto> GetProductsPageAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Fetches several pages concurrently and returns them in the order requested.</summary>
    Task<IReadOnlyList<PrologRecordsPageDto>> GetProductsPagesAsync(
        IEnumerable<int> pages, int pageSize, int maxConcurrency = 4, CancellationToken cancellationToken = default);
}

public class PrologApiClient(HttpClient httpClient) : IPrologApiClient
{
    public const string ApiKeyHeaderName = "X-Api-Key";
    private const string RecordsPath = "records";

    public const int MaxPageSize = 25;

    public async Task<PrologRecordsPageDto> GetProductsPageAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        // TODO: error handling (non-success status codes, timeouts, retries).
        var result = await httpClient.GetFromJsonAsync<PrologRecordsPageDto>(
            $"{RecordsPath}?page={page}&pageSize={pageSize}", cancellationToken);

        return result ?? throw new InvalidOperationException("Prolog API returned an empty response body.");
    }

    public async Task<IReadOnlyList<PrologRecordsPageDto>> GetProductsPagesAsync(
        IEnumerable<int> pages, int pageSize, int maxConcurrency = 4, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        using var throttle = new SemaphoreSlim(maxConcurrency);

        var tasks = pages.Select(async page =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                return await GetProductsPageAsync(page, pageSize, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        return await Task.WhenAll(tasks);
    }
}
