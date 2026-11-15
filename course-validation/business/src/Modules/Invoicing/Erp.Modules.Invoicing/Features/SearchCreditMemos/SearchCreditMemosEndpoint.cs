using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal static class SearchCreditMemosEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/credit-memos", async ([AsParameters] SearchCreditMemosQuery query, SearchCreditMemosHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));

        app.MapGet("/credit-memos/{id:long}",
                async Task<Results<Ok<CreditMemoDetailDto>, NotFound>> (long id, SearchCreditMemosHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetCreditMemoQuery(id), ct) is { } memo ? TypedResults.Ok(memo) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));

        app.MapGet("/credit-memos/{id:long}/pdf",
                async Task<Results<FileContentHttpResult, NotFound>> (long id, PrintCreditMemoHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new PrintCreditMemoCommand(id), ct) is { } pdf
                        ? TypedResults.File(pdf.Content, "application/pdf", pdf.FileName)
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
    }
}
