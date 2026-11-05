using System.Diagnostics;
using System.Security.Authentication;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = FluentValidation.ValidationException;

namespace Erp.Api;

// One place that maps exceptions to HTTP statuses, answering with RFC 9457 problem+json.
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException          => (StatusCodes.Status400BadRequest, "Validation failed"),
            AuthenticationException      => (StatusCodes.Status401Unauthorized, "Authentication failed"),
            ForbiddenException           => (StatusCodes.Status403Forbidden, "Forbidden"),
            NotFoundException            => (StatusCodes.Status404NotFound, "Resource not found"),
            ConcurrencyException or
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Someone else changed this. Reload and try again."),
            DomainException              => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            _                            => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        // Never leak a stack trace outside development.
        var detail = status switch
        {
            StatusCodes.Status500InternalServerError when env.IsDevelopment() => ex.ToString(),
            StatusCodes.Status500InternalServerError                          => "An unexpected error occurred.",
            StatusCodes.Status409Conflict                                     => null,
            _                                                                 => ex.Message
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = ctx.Request.Path };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
        if (ex is ValidationException v)
            problem.Extensions["errors"] = v.Errors.Select(e => e.ErrorMessage).ToArray();

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
        return true;
    }
}
