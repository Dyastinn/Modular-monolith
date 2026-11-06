using Dapper;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record ListSpecialDiscountProgramsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<SpecialDiscountProgramDto>>;

internal sealed record SpecialDiscountProgramDto(string Code, string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive);

internal sealed class ListSpecialDiscountProgramsHandler(IDbConnectionFactory connections)
    : IQueryHandler<ListSpecialDiscountProgramsQuery, IReadOnlyList<SpecialDiscountProgramDto>>
{
    public async ValueTask<IReadOnlyList<SpecialDiscountProgramDto>> Handle(ListSpecialDiscountProgramsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<SpecialDiscountProgramDto>(new CommandDefinition(
            "SELECT * FROM sales.list_special_discount_programs(@IncludeInactive)", new { q.IncludeInactive }, cancellationToken: ct))).ToList();
    }
}

internal sealed record SaveSpecialDiscountProgramCommand(string Code, string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive)
    : ICommand;

internal sealed class SaveSpecialDiscountProgramValidator : AbstractValidator<SaveSpecialDiscountProgramCommand>
{
    public SaveSpecialDiscountProgramValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.IdLabel).NotEmpty().MaximumLength(50);
    }
}

internal sealed class SaveSpecialDiscountProgramHandler(SalesDbContext db) : ICommandHandler<SaveSpecialDiscountProgramCommand>
{
    public async ValueTask<Unit> Handle(SaveSpecialDiscountProgramCommand cmd, CancellationToken ct)
    {
        var program = await db.SpecialDiscountPrograms.FindAsync([cmd.Code.Trim().ToUpperInvariant()], ct);
        if (program is null)
            db.SpecialDiscountPrograms.Add(SpecialDiscountProgram.Define(cmd.Code, cmd.Name, cmd.IdLabel, cmd.Rate, cmd.VatExempt));
        else
            program.Update(cmd.Name, cmd.IdLabel, cmd.Rate, cmd.VatExempt, cmd.IsActive);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

internal sealed record ApplySpecialDiscountCommand(long OrderId, string ProgramCode, string HolderName, string IdNumber) : ICommand;

internal sealed class ApplySpecialDiscountValidator : AbstractValidator<ApplySpecialDiscountCommand>
{
    public ApplySpecialDiscountValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.ProgramCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.HolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdNumber).NotEmpty().MaximumLength(50);
    }
}

internal sealed class ApplySpecialDiscountHandler(SalesDbContext db, ICurrentUser currentUser, IInvoicingSetup invoicing)
    : ICommandHandler<ApplySpecialDiscountCommand>
{
    public async ValueTask<Unit> Handle(ApplySpecialDiscountCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        var program = await db.SpecialDiscountPrograms.FindAsync([cmd.ProgramCode.Trim().ToUpperInvariant()], ct)
            ?? throw new NotFoundException($"There is no special discount program '{cmd.ProgramCode}'.");

        order.ApplySpecialDiscount(program.Grant(cmd.HolderName, cmd.IdNumber, await invoicing.GetTaxPolicyAsync(ct)));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

internal sealed record RemoveSpecialDiscountCommand(long OrderId) : ICommand;

internal sealed class RemoveSpecialDiscountHandler(SalesDbContext db, ICurrentUser currentUser)
    : ICommandHandler<RemoveSpecialDiscountCommand>
{
    public async ValueTask<Unit> Handle(RemoveSpecialDiscountCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        order.RemoveSpecialDiscount();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
