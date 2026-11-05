using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed class SearchInvoicesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/invoices",
                async (long? customerId, string? status, string? search, bool? overdueOnly, int? page, int? pageSize,
                       ISender sender, CancellationToken ct) =>
                    Results.Ok(await sender.Send(
                        new SearchInvoicesQuery(customerId, status, search, overdueOnly ?? false, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
}
