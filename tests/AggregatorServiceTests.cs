using prolog_mobile_api.DTOs;
using prolog_mobile_api.Services;

namespace prolog_mobile_api.Tests;

// Pure unit tests of the business rules: no HTTP, no DI, no async.
public class AggregatorServiceTests
{
    private readonly AggregatorService _sut = new();

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static PrologProductDto Item(int id, string category, decimal price = 1m, int qty = 1, DateTimeOffset? updated = null) =>
        new(id, $"Item {id}", category, price, qty, updated ?? T0);

    [Fact]
    public void Groups_categories_regardless_of_casing_and_whitespace_and_title_cases_them()
    {
        var result = _sut.Summarize(
        [
            Item(1, "  home & kitchen "),
            Item(2, "HOME & KITCHEN"),
            Item(3, " Home & Kitchen"),
        ]);

        var category = Assert.Single(result);
        Assert.Equal("Home & Kitchen", category.Category);
        Assert.Equal(3, category.Items.Count);
        Assert.All(category.Items, i => Assert.Equal("Home & Kitchen", i.Category)); // items carry the cleaned name
    }

    [Fact]
    public void Total_value_is_sum_of_price_times_quantity_with_exact_decimal_math()
    {
        var result = _sut.Summarize([Item(1, "a", price: 0.1m, qty: 3), Item(2, "a", price: 0.2m, qty: 1)]);

        Assert.Equal(0.5m, result.Single().TotalInventoryValue); // 0.1*3 + 0.2: would drift with double
    }

    [Fact]
    public void Zero_quantity_items_are_listed_but_not_counted_as_available()
    {
        var result = _sut.Summarize(
        [
            Item(1, "tools", price: 10m, qty: 2),
            Item(2, "tools", price: 999m, qty: 0),
        ]);

        var tools = Assert.Single(result);
        Assert.Equal(2, tools.Items.Count);
        Assert.Equal(1, tools.AvailableRecordCount);
        Assert.Equal(20m, tools.TotalInventoryValue);
    }

    [Fact]
    public void Category_with_only_zero_quantity_items_still_appears()
    {
        var result = _sut.Summarize([Item(1, "Discontinued", qty: 0)]);

        var discontinued = Assert.Single(result);
        Assert.Equal(0, discontinued.AvailableRecordCount);
        Assert.Equal(0m, discontinued.TotalInventoryValue);
    }

    [Fact]
    public void Most_recently_updated_picks_latest_including_zero_quantity_items()
    {
        var result = _sut.Summarize(
        [
            Item(1, "a", qty: 5, updated: T0),
            Item(2, "a", qty: 0, updated: T0.AddDays(3)),
        ]);

        var mostRecent = result.Single().MostRecentlyUpdated;
        Assert.Equal(2, mostRecent.Id);
        Assert.Equal("Item 2", mostRecent.Name);
        Assert.Equal(T0.AddDays(3), mostRecent.LastUpdated);
    }

    [Fact]
    public void Most_recently_updated_ties_are_broken_by_lowest_id()
    {
        var result = _sut.Summarize(
        [
            Item(5, "a", updated: T0.AddDays(2)),
            Item(3, "a", updated: T0.AddDays(2)),
            Item(1, "a", updated: T0),
        ]);

        Assert.Equal(3, result.Single().MostRecentlyUpdated.Id);
    }

    [Fact]
    public void Categories_are_ordered_by_name_and_items_by_id()
    {
        var result = _sut.Summarize([Item(3, "Toys"), Item(2, "Electronics"), Item(1, "Toys")]);

        Assert.Equal(["Electronics", "Toys"], result.Select(c => c.Category));
        Assert.Equal([1, 3], result.Single(c => c.Category == "Toys").Items.Select(i => i.Id));
    }

    [Fact]
    public void Empty_input_gives_empty_result()
    {
        Assert.Empty(_sut.Summarize([]));
    }
}
