# Prolog Mobile API

An ASP.NET Core (.NET 10) API that fetches every product record from the Prolog API, concurrently
across pages, and returns them grouped into per-category inventory summaries.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or later (pinned in `global.json`)

## Configuration

The API needs the Prolog API's base URL and API key. Both are required: the app refuses to start
without them. Keep them out of source control by using
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) locally:

```bash
dotnet user-secrets set "PrologApi:BaseUrl" "https://stg.prologmobile.com/api"
dotnet user-secrets set "PrologApi:ApiKey" "<your-api-key>"
```

User secrets are only loaded in the `Development` environment, which `dotnet run` uses by default.
Anywhere else, set environment variables instead:

```bash
export PrologApi__BaseUrl="https://stg.prologmobile.com/api"
export PrologApi__ApiKey="<your-api-key>"
```

Retry and timeout settings for calls to the Prolog API are under `PrologApi:Resilience` in
`appsettings.json`.

## Running the API

From the project root:

```bash
dotnet run
```

The API listens on `http://localhost:5286`.

| Endpoint | Description |
|---|---|
| `GET /api/prolog/health` | Returns `200 {"status":"online"}` while the API is running. Does not call the Prolog API. |
| `GET /api/prolog` | Returns every record grouped by category (see below). |

```bash
curl http://localhost:5286/api/prolog/health
curl http://localhost:5286/api/prolog
```

> **Expect `GET /api/prolog` to take about 10 seconds.** The Prolog API allows 5 requests per
> 10 seconds, and fetching all records takes more than 5 page requests. Requests over the limit
> are retried automatically after the `Retry-After` delay the API sends.

### Response

An array of categories, ordered by name:

```json
[
  {
    "category": "Electronics",
    "totalInventoryValue": 18253.18,
    "availableRecordCount": 25,
    "mostRecentlyUpdated": { "id": 1037, "name": "...", "lastUpdated": "..." },
    "items": [
      { "id": 1001, "name": "...", "category": "Electronics", "price": 4.99, "quantity": 0, "lastUpdated": "..." }
    ]
  }
]
```

How categories are cleaned up and how each figure is calculated is documented in
[ASSUMPTIONS.md](ASSUMPTIONS.md).

### Errors

If the Prolog API fails, the response is an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)
ProblemDetails body (`application/problem+json`) with a `reason` field:

| Status | `reason` | When |
|---|---|---|
| 502 | `Unauthorized` | The Prolog API rejected the API key |
| 502 | `Unreachable` | The Prolog API could not be reached |
| 502 | `ErrorResponse` | The Prolog API returned another error status |
| 502 | `InvalidResponse` | The Prolog API returned a body that isn't the expected JSON |
| 503 | `RateLimited` | Still rate-limited after retrying; try again shortly |
| 503 | `CircuitOpen` | Too many recent failures; calls are paused briefly |
| 504 | `Timeout` | The Prolog API didn't respond in time |

If any page fails, the whole request fails, so partial data never produces wrong totals.

## Running the tests

```bash
dotnet test --project tests
```

Or from inside the `tests` folder, just `dotnet test`.

The tests don't call the real Prolog API, so they need no secrets or network access.

| File | Covers |
|---|---|
| `tests/AggregatorServiceTests.cs` | Category grouping and calculations |
| `tests/PrologApiClientTests.cs` | Paging, concurrency limit, retries, timeouts and error translation, using the real HTTP pipeline with a fake network |
| `tests/PrologApiTests.cs` | The HTTP endpoints end to end, running the whole API in memory with a stubbed Prolog client |

## Project layout

```
Clients/      Prolog API client: paging, concurrency, error translation, HTTP setup
Config/       Strongly typed settings (PrologApiOptions)
Controllers/  HTTP endpoints
DTOs/         Shapes received from the Prolog API
Middleware/   Turns Prolog API failures into ProblemDetails responses
Models/       Shapes returned to the mobile app
Services/     PrologService (orchestration) and AggregatorService (category summaries)
tests/        xUnit test project
```
