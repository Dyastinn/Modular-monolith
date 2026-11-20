using Yasda.Modules.Identity.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal sealed record RenameTeamRequest(string Name);

internal sealed record SetTeamNotifyRequest(NotifyMode Mode, List<long>? SelectedUserIds);

internal sealed class RenameTeamRequestValidator : AbstractValidator<RenameTeamRequest>
{
    public RenameTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Team.NameMaxLength);
    }
}

internal sealed class SetTeamNotifyRequestValidator : AbstractValidator<SetTeamNotifyRequest>
{
    public SetTeamNotifyRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
    }
}

internal static class UpdateTeamEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/teams/{id:long}", async Task<Results<NoContent, NotFound>> (long id, RenameTeamRequest body, RenameTeamHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new RenameTeamCommand(id, body.Name), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));

        app.MapPost("/teams/{id:long}/activate", async Task<Results<NoContent, NotFound>> (long id, SetTeamActiveHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new SetTeamActiveCommand(id, true), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));

        app.MapPost("/teams/{id:long}/deactivate", async Task<Results<NoContent, NotFound>> (long id, SetTeamActiveHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new SetTeamActiveCommand(id, false), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));

        app.MapPut("/teams/{id:long}/notify", async Task<Results<NoContent, NotFound>> (long id, SetTeamNotifyRequest body, SetTeamNotifyHandler handler, CancellationToken ct) =>
        {
            var selected = body.SelectedUserIds ?? [];
            var found = await handler.HandleAsync(new SetTeamNotifyCommand(id, body.Mode, selected), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Managers));
    }
}
