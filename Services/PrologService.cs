using prolog_mobile_api.Clients;
using prolog_mobile_api.Models;

namespace prolog_mobile_api.Services;

public class PrologService(IPrologApiClient prologApiClient, AggregatorService aggregator)
{
    public async Task<CategorySummary[]> GetCategorySummariesAsync(CancellationToken cancellationToken = default)
    {
        var products = await prologApiClient.GetAllProductsAsync(cancellationToken);
        return aggregator.Summarize(products);
    }
}
