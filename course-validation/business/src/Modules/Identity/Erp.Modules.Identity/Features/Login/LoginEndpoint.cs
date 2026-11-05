using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

// The wire shape. Kept separate from LoginCommand so the HTTP contract and the command can evolve independently.
internal sealed record LoginRequest(string Username, string Password);

internal sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", async (LoginRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new LoginCommand(body.Username, body.Password), ct)))
        .AllowAnonymous();   // login can't require a token — that's what it hands out
}
