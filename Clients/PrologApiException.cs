namespace prolog_mobile_api.Clients;

public enum PrologApiFailure
{
    Unreachable,     // DNS, connection refused, TLS, etc.
    Timeout,
    CircuitOpen,     // too many recent failures; not even trying
    RateLimited,     // 429 still returned after retries were exhausted
    Unauthorized,    // 401/403: bad or missing API key, retrying won't help
    ErrorResponse,   // any other non-success status
    InvalidResponse, // 2xx but body is missing or not the JSON we expect
}

// The only exception type the client lets escape. Callers never see HttpRequestException,
// Polly exceptions or JsonException, only this, with a reason they can act on.
public sealed class PrologApiException(
    PrologApiFailure failure, string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public PrologApiFailure Failure { get; } = failure;
    public int? StatusCode { get; } = statusCode;
}
