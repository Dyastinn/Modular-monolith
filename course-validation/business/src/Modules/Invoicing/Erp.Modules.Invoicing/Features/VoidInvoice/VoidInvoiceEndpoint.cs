using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal static class VoidInvoiceEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id:long}/void",
                async Task<Results<NoContent, NotFound>> (long id, VoidRequest body, VoidInvoiceHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new VoidInvoiceCommand(id, body.Reason), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/invoices/{id:long}/reissue",
                async Task<Results<Created<CreatedId>, NotFound>> (long id, ReissueInvoiceHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ReissueInvoiceCommand(id), ct) is { } newId
                        ? TypedResults.Created($"/api/invoices/{newId}", new CreatedId(newId))
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}