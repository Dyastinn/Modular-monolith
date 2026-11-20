using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal sealed record AddTeamMemberRequest(long UserId);

internal sealed class AddTeamMemberRequestValidator : AbstractValidator<AddTeamMemberRequest>
{
    public AddTeamMemberRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
    }
}

internal static class TeamMembersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/teams/{id:long}/members", async Task<Results<NoContent, NotFound>> (long id, AddTeamMemberRequest body, AddTeamMemberHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new AddTeamMemberCommand(id, body.UserId), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));

        app.MapDelete("/teams/{id:long}/members/{userId:long}", async Task<Results<NoContent, NotFound>> (long id, long userId, RemoveTeamMemberHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new RemoveTeamMemberCommand(id, userId), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));
    }
}
