using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record AddArticleEditorRequest(long UserId);

internal sealed class AddArticleEditorRequestValidator : AbstractValidator<AddArticleEditorRequest>
{
    public AddArticleEditorRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
    }
}

internal static class ArticleEditorsEndpoint
{
    // Every writer may try; the article lets only its author, the author's team leads, KbEditors and Admins change the list.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/articles/{id:long}/editors", async Task<Results<NoContent, NotFound>> (long id, AddArticleEditorRequest body, AddArticleEditorHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new AddArticleEditorCommand(id, body.UserId), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));

        app.MapDelete("/articles/{id:long}/editors/{userId:long}", async Task<Results<NoContent, NotFound>> (long id, long userId, RemoveArticleEditorHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new RemoveArticleEditorCommand(id, userId), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));
    }
}
