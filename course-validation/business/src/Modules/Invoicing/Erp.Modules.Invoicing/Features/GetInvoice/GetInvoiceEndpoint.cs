using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed class GetInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices/{id:long}", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetInvoiceQuery(id), ct) is { } invoice ? Results.Ok(invoice) : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));

        app.MapGet("/invoices/{id:long}/pdf", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetInvoicePdfQuery(id), ct) is { } pdf
                    ? Results.File(pdf.Content, "application/pdf", pdf.FileName)
                    : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
    }
}
