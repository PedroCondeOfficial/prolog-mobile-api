using System.Globalization;
using prolog_mobile_api.DTOs;
using prolog_mobile_api.Models;

namespace prolog_mobile_api.Services;

// Pure business logic: data in, data out. No HTTP, no I/O.
public class AggregatorService
{
    public CategorySummary[] Summarize(IEnumerable<PrologProductDto> products) =>
        products
            // Upstream categories vary in casing and whitespace ("  home & kitchen ", "HOME & KITCHEN"),
            // so normalise before grouping. Items carry the cleaned name too.
            .Select(p => p with { Category = NormalizeCategory(p.Category) })
            .GroupBy(p => p.Category)
            .Select(g =>
            {
                var items = g.OrderBy(p => p.Id).ToList();

                // Newest first; ties broken by lowest Id so the result is deterministic.
                var mostRecent = items
                    .OrderByDescending(p => p.LastUpdated)
                    .ThenBy(p => p.Id)
                    .First();

                return new CategorySummary(
                    Category: g.Key,
                    TotalInventoryValue: items.Sum(p => p.Price * p.Quantity),
                    AvailableRecordCount: items.Count(p => p.Quantity > 0),
                    MostRecentlyUpdated: new MostRecentlyUpdatedItem(mostRecent.Id, mostRecent.Name, mostRecent.LastUpdated),
                    Items: items);
            })
            .OrderBy(s => s.Category)
            .ToArray();

    private static string NormalizeCategory(string category) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(category.Trim().ToLowerInvariant());
}
