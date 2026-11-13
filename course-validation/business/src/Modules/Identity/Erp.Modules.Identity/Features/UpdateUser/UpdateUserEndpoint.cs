using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;


internal sealed record UpdateUserRequest(string FullName, string Role);
internal sealed record ResetUserPasswordRequest(string TemporaryPassword);

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
    }
}

internal sealed class ResetUserPasswordRequestValidator : AbstractValidator<ResetUserPasswordRequest>
{
    public ResetUserPasswordRequestValidator() => RuleFor(x => x.TemporaryPassword).NotEmpty();
}

internal static class UpdateUserEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/users/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, UpdateUserRequest body, UpdateUserHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new UpdateUserCommand(id, body.FullName, body.Role), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/disable",
                async Task<Results<NoContent, NotFound>> (long id, SetUserDisabledHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetUserDisabledCommand(id, true), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/enable",
                async Task<Results<NoContent, NotFound>> (long id, SetUserDisabledHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetUserDisabledCommand(id, false), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/reset-password",
                async Task<Results<NoContent, NotFound>> (long id, ResetUserPasswordRequest body, ResetUserPasswordHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ResetUserPasswordCommand(id, body.TemporaryPassword), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}