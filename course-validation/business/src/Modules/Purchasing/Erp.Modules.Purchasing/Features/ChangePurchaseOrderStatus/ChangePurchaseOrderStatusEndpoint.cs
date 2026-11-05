using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed class ChangePurchaseOrderStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        Map(app, "approve", PurchaseOrderAction.Approve, PurchasingRoles.Approvers);
        Map(app, "send", PurchaseOrderAction.Send, PurchasingRoles.Buyers);
        Map(app, "close", PurchaseOrderAction.Close, PurchasingRoles.Buyers);
        Map(app, "cancel", PurchaseOrderAction.Cancel, PurchasingRoles.Buyers);
    }

    private static void Map(IEndpointRouteBuilder app, string verb, PurchaseOrderAction action, string[] roles) =>
        app.MapPost($"/purchase-orders/{{id:long}}/{verb}", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ChangePurchaseOrderStatusCommand(id, action), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(roles));
}
