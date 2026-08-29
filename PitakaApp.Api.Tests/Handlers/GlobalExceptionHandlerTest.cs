using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PitakaApp.Api.Handlers;

namespace PitakaApp.Api.Tests.Handlers;

public class GlobalExceptionHandlerTest
{
    private static (GlobalExceptionHandler handler, CapturingProblemDetailsService problems) Build()
    {
        var problems = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, problems);
        return (handler, problems);
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_WritesConflictProblemDetails()
    {
        var (handler, problems) = Build();
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(httpContext, new DbUpdateConcurrencyException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, problems.LastContext!.ProblemDetails.Status);
        // Matches the body ControllerBase.Problem(statusCode: 409) produced at the old call sites.
        Assert.Equal("Conflict", problems.LastContext.ProblemDetails.Title);
        Assert.Equal(GlobalExceptionHandler.ConcurrencyConflictDetail, problems.LastContext.ProblemDetails.Detail);
        // pitaka-web matches this against /updated by another request/i to raise AccountModifiedError.
        Assert.Matches("updated by another request", problems.LastContext.ProblemDetails.Detail!);
    }

    [Fact]
    public async Task UnhandledException_WritesInternalServerErrorProblemDetails()
    {
        var (handler, problems) = Build();
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, problems.LastContext!.ProblemDetails.Status);
        Assert.Equal("An error occurred", problems.LastContext.ProblemDetails.Title);
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? LastContext { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            LastContext = context;
            return ValueTask.CompletedTask;
        }
    }
}
