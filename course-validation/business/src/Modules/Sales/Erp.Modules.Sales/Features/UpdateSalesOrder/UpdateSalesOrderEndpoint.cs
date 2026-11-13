using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderRequest(IReadOnlyList<OrderLineInput> Lines);

internal sealed class UpdateSalesOrderRequestValidator : AbstractValidator<UpdateSalesOrderRequest>
{
    public UpdateSalesOrderRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineInputValidator());
    }
}

internal static class UpdateSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, UpdateSalesOrderRequest body, UpdateSalesOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new UpdateSalesOrderCommand(id, body.Lines), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}