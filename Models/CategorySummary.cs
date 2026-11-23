using prolog_mobile_api.DTOs;

namespace prolog_mobile_api.Models;

public record CategorySummary(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    MostRecentlyUpdatedItem MostRecentlyUpdated,
    IReadOnlyList<PrologProductDto> Items);

public record MostRecentlyUpdatedItem(int Id, string Name, DateTimeOffset LastUpdated);
