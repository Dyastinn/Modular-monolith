using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record PurchaseOrderRequest(long SupplierId, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines);

internal sealed class PurchaseOrderRequestValidator : AbstractValidator<PurchaseOrderRequest>
{
    public PurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A purchase order needs at least one line.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

internal static class SavePurchaseOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/purchase-orders", async Task<Created<CreatedId>> (PurchaseOrderRequest body, SavePurchaseOrderHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new SavePurchaseOrderCommand(null, body.SupplierId, body.ExpectedDate, body.Notes, body.Lines), ct);
                return TypedResults.Created($"/api/purchase-orders/{id}", new CreatedId(id!.Value));
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPut("/purchase-orders/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, PurchaseOrderRequest body, SavePurchaseOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SavePurchaseOrderCommand(id, body.SupplierId, body.ExpectedDate, body.Notes, body.Lines), ct) is null
                        ? TypedResults.NotFound()
                        : TypedResults.NoContent())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));
    }
}