using Yasda.Modules.Tickets.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record UpdateCategoryRequest(string Name, long? TeamId, bool IsActive);

internal sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Category.NameMaxLength);
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null);
    }
}

internal static class UpdateCategoryEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/categories/{id:long}", async Task<Results<NoContent, NotFound>> (long id, UpdateCategoryRequest body, UpdateCategoryHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new UpdateCategoryCommand(id, body.Name, body.TeamId, body.IsActive), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Leads));
    }
}
