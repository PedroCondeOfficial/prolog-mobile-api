# Assumptions

Decisions made where the requirements didn't specify behaviour. Each is isolated in
`Services/AggregatorService.cs` and can be changed independently.

## Category aggregation

1. **Categories are normalised before grouping.**
   The upstream API returns the same category with inconsistent casing and whitespace
   (e.g. `"  home & kitchen "`, `"HOME & KITCHEN"`, `"Home & Kitchen"`: 36 spellings of 6 categories).
   Categories are trimmed, grouped case-insensitively, and displayed in title case
   (`Home & Kitchen`). Items in the response carry the normalised category name.

2. **Zero-quantity items are kept.**
   They appear in a category's `items` and contribute `0` to `totalInventoryValue`.
   `availableRecordCount` counts only items with `quantity > 0`.

3. **`totalInventoryValue` is `sum(price × quantity)`** over a category's items, using `decimal`
   to avoid floating-point rounding on currency.

4. **`mostRecentlyUpdated` considers every item in the category**, including those with zero quantity.

5. **"Discontinued" is treated as a regular category.**
   It arrives in the `category` field like any other value, so it is grouped the same way, even though
   it reads more like a status (all of its items currently have zero quantity).

6. **Results are deterministic.**
   Categories are ordered by name, items within a category by `id`, and ties on `lastUpdated` for
   `mostRecentlyUpdated` are broken by the lowest `id`.
