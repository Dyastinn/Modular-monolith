using Yasda.Modules.Knowledge.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record RejectArticleRequest(string Reason);

internal sealed class RejectArticleRequestValidator : AbstractValidator<RejectArticleRequest>
{
    public RejectArticleRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Article.ReasonMaxLength);
    }
}

internal static class ChangeArticleStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Any writer asks for a review (the article checks they may touch this one); only KbEditors and Admins decide.
        app.MapPost("/articles/{id:long}/submit", async Task<Results<NoContent, NotFound>> (long id, ChangeArticleStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeArticleStatusCommand(id, ArticleStep.Submit), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));

        app.MapPost("/articles/{id:long}/reject", async Task<Results<NoContent, NotFound>> (long id, RejectArticleRequest body, ChangeArticleStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeArticleStatusCommand(id, ArticleStep.Reject, body.Reason), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Editors));

        app.MapPost("/articles/{id:long}/publish", async Task<Results<NoContent, NotFound>> (long id, ChangeArticleStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeArticleStatusCommand(id, ArticleStep.Publish), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Editors));

        app.MapPost("/articles/{id:long}/archive", async Task<Results<NoContent, NotFound>> (long id, ChangeArticleStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeArticleStatusCommand(id, ArticleStep.Archive), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Editors));

        app.MapPost("/articles/{id:long}/restore", async Task<Results<NoContent, NotFound>> (long id, ChangeArticleStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeArticleStatusCommand(id, ArticleStep.Restore), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Editors));
    }

    private static Results<NoContent, NotFound> Answer(bool found)
    {
        if (!found) return TypedResults.NotFound();
        return TypedResults.NoContent();
    }
}
