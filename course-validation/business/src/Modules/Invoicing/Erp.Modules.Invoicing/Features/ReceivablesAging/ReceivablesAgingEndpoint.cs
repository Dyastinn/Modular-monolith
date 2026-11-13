using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal static class ReceivablesAgingEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/receivables-aging", async ([AsParameters] ReceivablesAgingQuery query, ReceivablesAgingHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
}