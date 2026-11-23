# Assumptions

Decisions made where the requirements didn't specify behaviour. Each is isolated in
`Services/AggregatorService.cs` and can be changed independently.

## Category aggregation

1. **Categories are normalised before grouping.**
   The upstream API returns the same category with inconsistent casing and whitespace
   (e.g. `"  home & kitchen "`, `"HOME & KITCHEN"`, `"Home & Kitchen"`: 36 spellings of 6 categories).
   Categories are trimmed, grouped case-insensitively, and displayed in title case
   (`Home & Kitchen`). Items in the response carry the normalised category name.

2. **Zero-quantity records are excluded.**
   Only records with `quantity > 0` are considered available. Records with zero (or negative)
   quantity are dropped before grouping, so they appear in no category's `items`, and don't
   count toward `totalInventoryValue`, `availableRecordCount` or `mostRecentlyUpdated`.
   `availableRecordCount` is therefore the number of items in the category.

3. **`totalInventoryValue` is `sum(price × quantity)`** over a category's items, using `decimal`
   to avoid floating-point rounding on currency.

4. **A category with no available records is omitted.**
   Because zero-quantity records are excluded, a category whose records are all out of stock has
   nothing to summarise and does not appear in the response. In the current data this applies to
   "Discontinued", whose records all have zero quantity.

5. **Results are deterministic.**
   Categories are ordered by name, items within a category by `id`, and ties on `lastUpdated` for
   `mostRecentlyUpdated` are broken by the lowest `id`.

## Limitations

The following were left out, mainly because this is going to be run locally and it's for this
single use case, but in a production one we would include them.

- **Caching.** Every request to `GET /api/prolog` fetches all records from the Prolog API again.
  Because the Prolog API allows 5 requests per 10 seconds, each response takes about 10 seconds
  and uses up the rate limit. In production, the summaries would be cached for a short time.

- **Rate limiting on our own endpoint.** Nothing limits how often clients can call this API, so
  several clients calling at once would compete for the Prolog API's rate limit and get slower or
  fail with `503 RateLimited`.

- **Authentication.** The endpoints are open to anyone who can reach the API.

- **Paging our own response.** `GET /api/prolog` always returns every category with all of its
  items in a single response.

- **Health check depth.** `GET /api/prolog/health` only reports that this API is running; it
  doesn't check whether the Prolog API is reachable.
