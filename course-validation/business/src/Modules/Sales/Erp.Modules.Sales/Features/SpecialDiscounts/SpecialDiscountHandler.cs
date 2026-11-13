using Dapper;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

internal sealed record ListSpecialDiscountProgramsQuery(bool IncludeInactive = false);

internal sealed record SpecialDiscountProgramDto(string Code, string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive);

internal sealed class ListSpecialDiscountProgramsHandler(IDbConnectionFactory connections)
{
    public async Task<IReadOnlyList<SpecialDiscountProgramDto>> HandleAsync(ListSpecialDiscountProgramsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<SpecialDiscountProgramDto>(new CommandDefinition(
            "SELECT * FROM sales.list_special_discount_programs(@IncludeInactive)", new { q.IncludeInactive }, cancellationToken: ct))).ToList();
    }
}

internal sealed record SaveSpecialDiscountProgramCommand(string Code, string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive);

internal sealed class SaveSpecialDiscountProgramHandler(SalesDbContext db)
{
    public async Task HandleAsync(SaveSpecialDiscountProgramCommand cmd, CancellationToken ct)
    {
        var program = await db.SpecialDiscountPrograms.FindAsync([cmd.Code.Trim().ToUpperInvariant()], ct);
        if (program is null)
            db.SpecialDiscountPrograms.Add(SpecialDiscountProgram.Define(cmd.Code, cmd.Name, cmd.IdLabel, cmd.Rate, cmd.VatExempt));
        else
            program.Update(cmd.Name, cmd.IdLabel, cmd.Rate, cmd.VatExempt, cmd.IsActive);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed record ApplySpecialDiscountCommand(long OrderId, string ProgramCode, string HolderName, string IdNumber);

internal sealed class ApplySpecialDiscountHandler(SalesDbContext db, ICurrentUser currentUser, IInvoicingSetup invoicing)
{
    public async Task<bool> HandleAsync(ApplySpecialDiscountCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        var program = await db.SpecialDiscountPrograms.FindAsync([cmd.ProgramCode.Trim().ToUpperInvariant()], ct)
            ?? throw new DomainException(SalesErrors.DiscountProgramNotFound, $"There is no special discount program '{cmd.ProgramCode}'.");

        order.ApplySpecialDiscount(program.Grant(cmd.HolderName, cmd.IdNumber, await invoicing.GetTaxPolicyAsync(ct)));
        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record RemoveSpecialDiscountCommand(long OrderId);

internal sealed class RemoveSpecialDiscountHandler(SalesDbContext db, ICurrentUser currentUser)
{
    public async Task<bool> HandleAsync(RemoveSpecialDiscountCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        order.RemoveSpecialDiscount();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
