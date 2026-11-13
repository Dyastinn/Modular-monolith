using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;


internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

internal static class ChangePasswordEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/change-password", async Task<NoContent> (ChangePasswordRequest body, ChangePasswordHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new ChangePasswordCommand(body.CurrentPassword, body.NewPassword), ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization();
}