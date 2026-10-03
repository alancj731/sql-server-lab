using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SqlServerLab.Application.Errors;

namespace SqlServerLab.Api.Http;

/// <summary>Maps application exceptions to RFC 9457 problem details. Unexpected errors never leak internals.</summary>
public sealed partial class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            ValidationFailedException v => new ValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Type = "https://sqlserverlab.dev/problems/validation",
            },
            NotFoundException n => Problem(StatusCodes.Status404NotFound, "Not found", n.Message, "not-found"),
            QuotaExceededException q => Problem(StatusCodes.Status409Conflict, "Quota exceeded", q.Message, "quota"),
            ConflictException c => Problem(StatusCodes.Status409Conflict, "Conflict", c.Message, "conflict"),
            BadHttpRequestException b => Problem(b.StatusCode, "Bad request", "The request body or parameters are malformed.", "bad-request"),
            _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred. Quote the correlation ID when reporting it.", "unexpected"),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(exception);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Problem(int status, string title, string detail, string type) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = $"https://sqlserverlab.dev/problems/{type}",
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandled(Exception ex);
}
