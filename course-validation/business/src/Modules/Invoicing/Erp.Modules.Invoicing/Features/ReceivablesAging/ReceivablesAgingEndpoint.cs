using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed class ReceivablesAgingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/receivables-aging", async (DateOnly? asOf, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ReceivablesAgingQuery(asOf), ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
}
