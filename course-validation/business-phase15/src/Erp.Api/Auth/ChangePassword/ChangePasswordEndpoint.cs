using Carter;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Api.Auth;

public sealed record ChangePasswordRequest(string Username, string CurrentPassword, string NewPassword);

public sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        // Anonymous like /auth/login: the current password is the credential, and this must work even when
        // the password has expired (so a valid access token can't be a prerequisite for recovering).
        app.MapPost("/auth/change-password", async (ChangePasswordRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangePasswordCommand(body.Username, body.CurrentPassword, body.NewPassword), ct);
            return Results.NoContent();
        })
        .AllowAnonymous();
}
