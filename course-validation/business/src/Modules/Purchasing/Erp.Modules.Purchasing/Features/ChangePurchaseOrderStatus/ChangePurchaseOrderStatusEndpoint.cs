using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal static class ChangePurchaseOrderStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        Map(app, "approve", PurchaseOrderAction.Approve, PurchasingRoles.Approvers);
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