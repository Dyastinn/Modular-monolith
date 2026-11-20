using Yasda.Modules.Identity.Domain;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal sealed record CreateTeamRequest(string Name);

internal sealed class CreateTeamRequestValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Team.NameMaxLength);
    }
}

internal static class CreateTeamEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/teams", async Task<Created<CreatedId>> (CreateTeamRequest body, CreateTeamHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateTeamCommand(body.Name), ct);
            return TypedResults.Created($"/api/teams/{id}", new CreatedId(id));
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));
    }
}
