using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed class VoidInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id:long}/void", async (long id, VoidRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new VoidInvoiceCommand(id, body.Reason), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/invoices/{id:long}/reissue", async (long id, ISender sender, CancellationToken ct) =>
            {
                var newId = await sender.Send(new ReissueInvoiceCommand(id), ct);
                return Results.Created($"/api/invoices/{newId}", new { id = newId });
            })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}
