using System.Security.Authentication;
using Erp.Shared.BuildingBlocks;
using Erp.Shared.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using ValidationException = FluentValidation.ValidationException;

namespace Erp.Api;

// Maps each exception type to an HTTP status, in one place (Phase 11 §5).
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
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

        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        var problem = new
        {
            title,
            status,
            detail = status == 500 ? "An unexpected error occurred." : ex.Message,   // don't leak internals on 500
            errors = ex is ValidationException v ? v.Errors.Select(e => e.ErrorMessage) : null
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;   // we handled it
    }
}
