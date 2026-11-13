using System.Diagnostics;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

// One place that maps exceptions to HTTP statuses, answering with RFC 9457 problem+json. Only what is thrown lands
// here: a business rule refused, two people changing the same thing, or a bug. A bad request or a missing record is
// answered by its endpoint without an exception.
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, error) = ex switch
        {
            // Malformed JSON, a missing body: the request never reached a handler.
            BadHttpRequestException bad  => (bad.StatusCode, CommonErrors.UnreadableRequest),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, CommonErrors.ChangedBySomeoneElse),
            // A unique index caught what a check-then-insert missed: two people creating the same thing at once.
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } }
                                         => (StatusCodes.Status409Conflict, CommonErrors.AlreadyExists),
            DomainException domain       => (StatusCodes.Status422UnprocessableEntity, domain.Error),
            _                            => (StatusCodes.Status500InternalServerError, CommonErrors.Unexpected)
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

        var problem = new ProblemDetails { Status = status, Detail = detail, Instance = ctx.Request.Path };
        ErrorProblems.Apply(problem, error);
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

        ctx.Response.StatusCode = status;
        // charset spelled out: some clients (Windows PowerShell 5.1) don't assume UTF-8, and ₱ arrives garbled.
        await ctx.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json; charset=utf-8", ct);
        return true;
    }
}
