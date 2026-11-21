using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record CreateArticleRequest(string Title, string Summary, string Body, ArticleVisibility Visibility = ArticleVisibility.Public);

internal sealed class CreateArticleRequestValidator : AbstractValidator<CreateArticleRequest>
{
    public CreateArticleRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Article.TitleMaxLength);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(Article.SummaryMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Article.BodyMaxLength);
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

internal static class CreateArticleEndpoint
{
    // Any staff member may start a draft; a KbEditor or Admin publishes it.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/articles", async Task<Created<CreatedId>> (CreateArticleRequest body, CreateArticleHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateArticleCommand(body.Title, body.Summary, body.Body, body.Visibility), ct);
            return TypedResults.Created($"/api/articles/{id}", new CreatedId(id));
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));
    }
}
