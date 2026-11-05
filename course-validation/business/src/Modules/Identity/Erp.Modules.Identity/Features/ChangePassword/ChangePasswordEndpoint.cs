using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed record ChangePasswordRequest(string Username, string CurrentPassword, string NewPassword);

internal sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        // Anonymous like /auth/login, so a user whose password has expired can still recover.
        app.MapPost("/auth/change-password", async (ChangePasswordRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangePasswordCommand(body.Username, body.CurrentPassword, body.NewPassword), ct);
            return Results.NoContent();
        })
        .AllowAnonymous();
}
