using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record ReceivePurchaseOrderRequest(IReadOnlyList<ReceivedLineInput> Lines);

internal sealed class ReceivePurchaseOrderRequestValidator : AbstractValidator<ReceivePurchaseOrderRequest>
{
    public ReceivePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Enter at least one received quantity.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
        });
    }
}

internal static class ReceivePurchaseOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/purchase-orders/{id:long}/receipts",
                async Task<Results<NoContent, NotFound>> (long id, ReceivePurchaseOrderRequest body, ReceivePurchaseOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ReceivePurchaseOrderCommand(id, body.Lines), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Receivers));
}