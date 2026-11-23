using prolog_mobile_api.Clients;
using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Services;

public class PrologService(IPrologApiClient prologApiClient)
{
    // Single page for now; switch to prologApiClient.GetProductsPagesAsync once this is verified.
    public Task<PrologRecordsPageDto> GetProductsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return prologApiClient.GetProductsPageAsync(page, pageSize, cancellationToken);
    }
}
