using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;


internal static class GetInvoiceEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices/{id:long}",
                async Task<Results<Ok<InvoiceDetailDto>, NotFound>> (long id, GetInvoiceHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetInvoiceQuery(id), ct) is { } invoice ? TypedResults.Ok(invoice) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));

        app.MapGet("/invoices/{id:long}/pdf",
                async Task<Results<FileContentHttpResult, NotFound>> (long id, PrintInvoiceHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new PrintInvoiceCommand(id), ct) is { } pdf
                        ? TypedResults.File(pdf.Content, "application/pdf", pdf.FileName)
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
    }
}