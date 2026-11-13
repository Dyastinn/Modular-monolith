using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record CreateSalesOrderRequest(long CustomerId, IReadOnlyList<OrderLineInput> Lines);

internal sealed class OrderLineInputValidator : AbstractValidator<OrderLineInput>
{
    public OrderLineInputValidator()
    {
        RuleFor(l => l.ItemId).GreaterThan(0);
        RuleFor(l => l.Quantity).ValidQuantity();
    }
}

internal sealed class CreateSalesOrderRequestValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineInputValidator());
    }
}

internal static class CreateSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders",
                async Task<Created<CreatedId>> (CreateSalesOrderRequest body, CreateSalesOrderHandler handler, CancellationToken ct) =>
                {
                    var id = await handler.HandleAsync(new CreateSalesOrderCommand(body.CustomerId, body.Lines), ct);
                    return TypedResults.Created($"/api/sales-orders/{id}", new CreatedId(id));
                })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}