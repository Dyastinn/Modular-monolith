using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed record UpdateUserRequest(string FullName, string Role);
internal sealed record ResetUserPasswordRequest(string TemporaryPassword);

internal sealed class UpdateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/users/{id:long}", async (long id, UpdateUserRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateUserCommand(id, body.FullName, body.Role), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/disable", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetUserDisabledCommand(id, true), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/enable", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetUserDisabledCommand(id, false), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/reset-password", async (long id, ResetUserPasswordRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ResetUserPasswordCommand(id, body.TemporaryPassword), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
