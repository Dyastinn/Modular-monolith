using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CreateCategoryRequest(string Name, long? TeamId);

internal sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Category.NameMaxLength);
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null);
    }
}

internal static class CreateCategoryEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/categories", async Task<Created<CreatedId>> (CreateCategoryRequest body, CreateCategoryHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateCategoryCommand(body.Name, body.TeamId), ct);
            return TypedResults.Created($"/api/categories/{id}", new CreatedId(id));
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Leads));
    }
}
