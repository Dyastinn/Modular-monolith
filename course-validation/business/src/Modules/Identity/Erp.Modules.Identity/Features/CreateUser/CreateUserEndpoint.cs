using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;


internal sealed record CreateUserRequest(string Email, string FullName, string Role, string TemporaryPassword);

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.TemporaryPassword).NotEmpty();
    }
}

internal static class CreateUserEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/users", async Task<Created<CreatedId>> (CreateUserRequest body, CreateUserHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new CreateUserCommand(body.Email, body.FullName, body.Role, body.TemporaryPassword), ct);
                return TypedResults.Created($"/api/users/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}