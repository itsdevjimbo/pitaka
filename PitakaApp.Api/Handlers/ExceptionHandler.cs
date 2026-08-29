using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PitakaApp.Api.Handlers;

public class GlobalExceptionHandler: IExceptionHandler
{
    // The single source of truth for the 409 body raised when an optimistic-concurrency
    // check fails on a write. pitaka-web's account-lifecycle code matches this against
    // /updated by another request/i to raise AccountModifiedError, so that substring is a
    // load-bearing part of the contract: keep it intact if this copy is ever reworded.
    public const string ConcurrencyConflictDetail =
        "The record was updated by another request. Please try again.";

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetailsService;
    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetailsService
    )
    {
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = exception is DbUpdateConcurrencyException
            ? ConcurrencyConflict()
            : UnhandledError(exception);

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        };

        await _problemDetailsService.WriteAsync(context);
        return true;
    }

    // Title is pinned to match the body ControllerBase.Problem(statusCode: 409) produced at
    // the old call sites; Type is left for the ProblemDetails writer to fill from the status.
    private static ProblemDetails ConcurrencyConflict() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Conflict",
        Detail = ConcurrencyConflictDetail
    };

    private ProblemDetails UnhandledError(Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred"
        };
    }
}
