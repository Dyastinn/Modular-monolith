using System.Security.Authentication;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using ValidationException = FluentValidation.ValidationException;

namespace Erp.Api;

// Maps each exception type to an HTTP status, in one place (Phase 11 §5).
// IHostEnvironment tells us Development vs Production — the only thing that changes the 500 detail.
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> log,
    IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException     => (StatusCodes.Status400BadRequest,          "Validation failed"),
            NotFoundException       => (StatusCodes.Status404NotFound,            "Resource not found"),
            AuthenticationException => (StatusCodes.Status401Unauthorized,        "Authentication failed"),
            ForbiddenException      => (StatusCodes.Status403Forbidden,           "Forbidden"),
            ConcurrencyException    => (StatusCodes.Status409Conflict,            "Conflict — please reload"),
            DomainException         => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            _                       => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        // Always log full detail server-side, regardless of environment.
        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        // The 500 detail is the only environment-sensitive bit: the real stack trace while developing, a
        // bland message in production so we never leak internals. Business errors (422/404/409/…) carry
        // messages meant for the user, so they show verbatim in both.
        string? detail = status switch
        {
            StatusCodes.Status500InternalServerError when env.IsDevelopment() => ex.ToString(),
            StatusCodes.Status500InternalServerError                          => "An unexpected error occurred.",
            _                                                                 => ex.Message
        };

        var problem = new
        {
            title,
            status,
            detail,
            errors = ex is ValidationException v ? v.Errors.Select(e => e.ErrorMessage) : null
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;   // we handled it
    }
}
