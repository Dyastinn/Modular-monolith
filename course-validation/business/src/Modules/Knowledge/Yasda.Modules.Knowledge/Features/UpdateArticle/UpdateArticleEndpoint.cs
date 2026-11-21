using Yasda.Modules.Knowledge.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record UpdateArticleRequest(string Title, string Summary, string Body, ArticleVisibility Visibility);

internal sealed class UpdateArticleRequestValidator : AbstractValidator<UpdateArticleRequest>
{
    public UpdateArticleRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Article.TitleMaxLength);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(Article.SummaryMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Article.BodyMaxLength);
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

internal static class UpdateArticleEndpoint
{
    // Every writer may try; the article decides who really may (its author, the author's team leads, editors, its edit list).
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/articles/{id:long}", async Task<Results<NoContent, NotFound>> (long id, UpdateArticleRequest body, UpdateArticleHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new UpdateArticleCommand(id, body.Title, body.Summary, body.Body, body.Visibility), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));
    }
}
