using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;


internal static class SearchInvoicesEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/invoices", async ([AsParameters] SearchInvoicesQuery query, SearchInvoicesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
}