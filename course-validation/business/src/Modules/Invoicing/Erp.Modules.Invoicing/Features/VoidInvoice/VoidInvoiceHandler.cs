using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record VoidInvoiceCommand(long InvoiceId, string Reason) : ICommand;

internal sealed class VoidInvoiceValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VoidInvoiceHandler(InvoicingDbContext db, ICurrentUser user, IClock clock) : ICommandHandler<VoidInvoiceCommand>
{
    public async ValueTask<Unit> Handle(VoidInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");
        invoice.Void(cmd.Reason, new UserId(user.UserId), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// After voiding a wrong invoice, bill the same shipment again under a new number.
internal sealed record ReissueInvoiceCommand(long VoidInvoiceId) : ICommand<long>;

internal sealed class ReissueInvoiceHandler(InvoicingDbContext db, InvoiceIssuer issuer) : ICommandHandler<ReissueInvoiceCommand, long>
{
    public async ValueTask<long> Handle(ReissueInvoiceCommand cmd, CancellationToken ct)
    {
        var voided = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.VoidInvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.VoidInvoiceId} not found.");
        voided.EnsureCanBeReissued();
        return await issuer.IssueForShipmentAsync(voided.ShipmentId, voided.SalesOrderId, ct);
    }
}
