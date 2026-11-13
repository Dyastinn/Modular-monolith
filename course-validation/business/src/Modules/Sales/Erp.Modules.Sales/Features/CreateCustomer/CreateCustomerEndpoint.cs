using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal sealed class CustomerInputValidator : AbstractValidator<CustomerInput>
{
    public CustomerInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tin).MaximumLength(20);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
    }
}

internal static class CreateCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers", async Task<Created<CreatedId>> (CustomerInput body, CreateCustomerHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new CreateCustomerCommand(body), ct);
                return TypedResults.Created($"/api/customers/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}