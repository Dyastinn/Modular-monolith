using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal sealed record SaveSpecialDiscountProgramRequest(string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive = true);
internal sealed record ApplySpecialDiscountRequest(string ProgramCode, string HolderName, string IdNumber);

internal sealed class SaveSpecialDiscountProgramRequestValidator : AbstractValidator<SaveSpecialDiscountProgramRequest>
{
    public SaveSpecialDiscountProgramRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.IdLabel).NotEmpty().MaximumLength(50);
    }
}

internal sealed class ApplySpecialDiscountRequestValidator : AbstractValidator<ApplySpecialDiscountRequest>
{
    public ApplySpecialDiscountRequestValidator()
    {
        RuleFor(x => x.ProgramCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.HolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdNumber).NotEmpty().MaximumLength(50);
    }
}

internal static class SpecialDiscountEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/special-discount-programs",
                async ([AsParameters] ListSpecialDiscountProgramsQuery query, ListSpecialDiscountProgramsHandler handler, CancellationToken ct) =>
                    TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        // The code is part of the address, so the route checks its shape: 1 to 20 letters, digits, "-" or "_".
        app.MapPut("/special-discount-programs/{code:regex(^[A-Za-z0-9_-]{{1,20}}$)}",
                async Task<NoContent> (string code, SaveSpecialDiscountProgramRequest b, SaveSpecialDiscountProgramHandler handler, CancellationToken ct) =>
                {
                    await handler.HandleAsync(new SaveSpecialDiscountProgramCommand(code, b.Name, b.IdLabel, b.Rate, b.VatExempt, b.IsActive), ct);
                    return TypedResults.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPut("/sales-orders/{id:long}/special-discount",
                async Task<Results<NoContent, NotFound>> (long id, ApplySpecialDiscountRequest body, ApplySpecialDiscountHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ApplySpecialDiscountCommand(id, body.ProgramCode, body.HolderName, body.IdNumber), ct)
                        ? TypedResults.NoContent()
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));

        app.MapDelete("/sales-orders/{id:long}/special-discount",
                async Task<Results<NoContent, NotFound>> (long id, RemoveSpecialDiscountHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new RemoveSpecialDiscountCommand(id), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
    }
}