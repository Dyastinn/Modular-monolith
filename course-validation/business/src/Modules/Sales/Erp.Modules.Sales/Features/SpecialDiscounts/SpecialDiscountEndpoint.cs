using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record SaveSpecialDiscountProgramRequest(string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive = true);
internal sealed record ApplySpecialDiscountRequest(string ProgramCode, string HolderName, string IdNumber);

internal sealed class SpecialDiscountEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/special-discount-programs", async (bool? includeInactive, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListSpecialDiscountProgramsQuery(includeInactive ?? false), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        app.MapPut("/special-discount-programs/{code}",
                async (string code, SaveSpecialDiscountProgramRequest b, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new SaveSpecialDiscountProgramCommand(code, b.Name, b.IdLabel, b.Rate, b.VatExempt, b.IsActive), ct);
                    return Results.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPut("/sales-orders/{id:long}/special-discount",
                async (long id, ApplySpecialDiscountRequest body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new ApplySpecialDiscountCommand(id, body.ProgramCode, body.HolderName, body.IdNumber), ct);
                    return Results.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));

        app.MapDelete("/sales-orders/{id:long}/special-discount", async (long id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new RemoveSpecialDiscountCommand(id), ct);
                    return Results.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
    }
}
