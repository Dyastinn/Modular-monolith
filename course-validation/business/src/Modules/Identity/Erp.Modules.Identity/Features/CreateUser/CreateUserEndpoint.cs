using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class CreateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/users", async (CreateUserCommand body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(body, ct);
                return Results.Created($"/api/users/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
