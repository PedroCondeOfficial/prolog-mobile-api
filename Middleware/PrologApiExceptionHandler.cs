using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using prolog_mobile_api.Clients;

namespace prolog_mobile_api.Middleware;

// Turns Prolog API failures into a clean RFC 7807 ProblemDetails response instead of a 500
// with a stack trace. Anything that isn't a PrologApiException falls through to the
// default handler (generic 500 ProblemDetails).
public sealed class PrologApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<PrologApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not PrologApiException prologException)
        {
            return false;
        }

        logger.LogError(prologException, "Prolog API failure: {Failure}", prologException.Failure);

        var (status, title) = prologException.Failure switch
        {
            PrologApiFailure.Timeout => (StatusCodes.Status504GatewayTimeout, "The Prolog API timed out."),
            PrologApiFailure.CircuitOpen or PrologApiFailure.RateLimited =>
                (StatusCodes.Status503ServiceUnavailable, "The Prolog API is temporarily unavailable. Try again shortly."),
            _ => (StatusCodes.Status502BadGateway, "The Prolog API could not be reached or returned an error."),
        };

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = prologException,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["reason"] = prologException.Failure.ToString() },
            },
        });
    }
}
