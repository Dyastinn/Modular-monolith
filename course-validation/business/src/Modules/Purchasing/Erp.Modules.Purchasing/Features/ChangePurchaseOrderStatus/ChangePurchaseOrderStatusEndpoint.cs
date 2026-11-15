using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record RejectPurchaseOrderRequest(string Reason);

internal sealed class RejectPurchaseOrderRequestValidator : AbstractValidator<RejectPurchaseOrderRequest>
{
    public RejectPurchaseOrderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal static class ChangePurchaseOrderStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        Map(app, "submit", PurchaseOrderAction.Submit, PurchasingRoles.Buyers);
        Map(app, "approve", PurchaseOrderAction.Approve, PurchasingRoles.Approvers);

        app.MapPost("/purchase-orders/{id:long}/reject",
                async Task<Results<NoContent, NotFound>> (long id, RejectPurchaseOrderRequest body, RejectPurchaseOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new RejectPurchaseOrderCommand(id, body.Reason), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Approvers));
        Map(app, "send", PurchaseOrderAction.Send, PurchasingRoles.Buyers);
        Map(app, "close", PurchaseOrderAction.Close, PurchasingRoles.Buyers);
        Map(app, "cancel", PurchaseOrderAction.Cancel, PurchasingRoles.Buyers);
    }

    private static void Map(IEndpointRouteBuilder app, string verb, PurchaseOrderAction action, string[] roles) =>
        app.MapPost($"/purchase-orders/{{id:long}}/{verb}",
                async Task<Results<NoContent, NotFound>> (long id, ChangePurchaseOrderStatusHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ChangePurchaseOrderStatusCommand(id, action), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(roles));
}