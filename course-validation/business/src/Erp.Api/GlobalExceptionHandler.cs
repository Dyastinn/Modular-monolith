using System.Diagnostics;
using System.Security.Authentication;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = FluentValidation.ValidationException;

namespace Erp.Api;

// Maps each exception type to an HTTP status, in one place, and answers with RFC 9457 problem+json.
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException             => (StatusCodes.Status400BadRequest,          "Validation failed"),
            AuthenticationException         => (StatusCodes.Status401Unauthorized,        "Authentication failed"),
            ForbiddenException              => (StatusCodes.Status403Forbidden,           "Forbidden"),
            NotFoundException               => (StatusCodes.Status404NotFound,            "Resource not found"),
            ConcurrencyException or
            DbUpdateConcurrencyException    => (StatusCodes.Status409Conflict,            "Conflict — someone else changed this. Reload and try again."),
            DomainException                 => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            _                               => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        // Business errors carry messages meant for the user. A 500's detail is the stack trace only while
        // developing — in production it's bland, so we never leak internals.
        var detail = status switch
        {
            StatusCodes.Status500InternalServerError when env.IsDevelopment() => ex.ToString(),
            StatusCodes.Status500InternalServerError                          => "An unexpected error occurred.",
            StatusCodes.Status409Conflict                                     => null,
            _                                                                 => ex.Message
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = ctx.Request.Path };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier;   // quote this when reporting a bug
        if (ex is ValidationException v)
            problem.Extensions["errors"] = v.Errors.Select(e => e.ErrorMessage).ToArray();

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
        return true;
    }
}
