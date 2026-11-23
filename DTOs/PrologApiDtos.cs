namespace prolog_mobile_api.DTOs;

// DTOs for responses received from the Prolog API.

/// <summary>One page of the Prolog API's /records response.</summary>
public record PrologRecordsPageDto(
    IReadOnlyList<PrologProductDto> Records,
    int Page,
    int PageSize,
    int TotalPages,
    int TotalRecords);

/// <summary>A single product record as returned by the Prolog API.</summary>
public record PrologProductDto(
    int Id,
    string Name,
    string Category,
    decimal Price,               // Unit price, USD
    int Quantity,                // Units on hand; may be 0
    DateTimeOffset LastUpdated); // ISO 8601, UTC
