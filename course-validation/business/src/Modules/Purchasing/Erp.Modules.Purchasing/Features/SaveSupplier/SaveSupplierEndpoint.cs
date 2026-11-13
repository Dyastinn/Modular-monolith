using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed class SupplierInputValidator : AbstractValidator<SupplierInput>
{
    public SupplierInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tin).MaximumLength(20);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
    }
}

internal static class SaveSupplierEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/suppliers", async Task<Created<CreatedId>> (SupplierInput body, SaveSupplierHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new SaveSupplierCommand(null, body), ct);
                return TypedResults.Created($"/api/suppliers/{id}", new CreatedId(id!.Value));
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPut("/suppliers/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, SupplierInput body, SaveSupplierHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SaveSupplierCommand(id, body), ct) is null ? TypedResults.NotFound() : TypedResults.NoContent())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPost("/suppliers/{id:long}/activate",
                async Task<Results<NoContent, NotFound>> (long id, SetSupplierActiveHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetSupplierActiveCommand(id, true), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPost("/suppliers/{id:long}/deactivate",
                async Task<Results<NoContent, NotFound>> (long id, SetSupplierActiveHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetSupplierActiveCommand(id, false), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));
    }
}