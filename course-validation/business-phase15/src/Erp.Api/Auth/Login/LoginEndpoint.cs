using Carter;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Api.Auth;

// The wire shape — the JSON body this route accepts. Kept separate from LoginCommand so the HTTP contract
// and the mediator contract can evolve independently (the convention every endpoint follows; Phase 4b §6).
public sealed record LoginRequest(string Username, string Password);

public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", async (LoginRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new LoginCommand(body.Username, body.Password), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous();   // login must NOT require a token (chicken-and-egg)
}
