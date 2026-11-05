using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class SignOutEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SignOutCommand(), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/auth/logout-everywhere", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SignOutEverywhereCommand(), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
