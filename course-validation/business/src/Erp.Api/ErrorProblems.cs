using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api;

// Puts the error code into a problem response: "code" for programs, "type" as the link to its catalog page.
internal static class ErrorProblems
{
    // The framework's own errors (a bare 401, 403, 404, 429) carry no code of their own: their status decides it.
    public static ErrorCode ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => CommonErrors.ValidationFailed,
        StatusCodes.Status401Unauthorized => CommonErrors.NotSignedIn,
        StatusCodes.Status403Forbidden => CommonErrors.NotAllowed,
        StatusCodes.Status404NotFound => CommonErrors.NotFound,
        StatusCodes.Status409Conflict => CommonErrors.ChangedBySomeoneElse,
        StatusCodes.Status429TooManyRequests => CommonErrors.TooManyRequests,
        _ => CommonErrors.Unexpected
    };

    // The framework writes "application/problem+json" with no charset. Some clients (Windows PowerShell 5.1) then don't
    // assume UTF-8, and ₱ arrives garbled, so every problem body says it plainly.
    public static IApplicationBuilder UseUtf8ProblemBodies(this IApplicationBuilder app) =>
        app.Use((ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                if (ctx.Response.ContentType == "application/problem+json")
                    ctx.Response.ContentType = "application/problem+json; charset=utf-8";
                return Task.CompletedTask;
            });
            return next(ctx);
        });

    public static void Apply(ProblemDetails problem, ErrorCode error)
    {
        problem.Type = $"/errors/{error.Code}";
        problem.Title = error.Title;
        problem.Extensions["code"] = error.Code;
    }
}
