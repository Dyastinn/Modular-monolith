using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/change-password", async (ChangePasswordCommand body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(body, ct);
                return Results.NoContent();
            })
            .RequireAuthorization();
}
