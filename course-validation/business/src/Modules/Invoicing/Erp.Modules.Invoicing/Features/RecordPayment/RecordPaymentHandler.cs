using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record RecordPaymentCommand(long InvoiceId, decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference)
    : ICommand<long>;

internal sealed class RecordPaymentValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

// Saving the payment also writes PaymentRecorded to the outbox; Sales lowers the customer's balance from it.
internal sealed class RecordPaymentHandler(InvoicingDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<RecordPaymentCommand, long>
{
    public async ValueTask<long> Handle(RecordPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");

        var paymentId = await db.NextPaymentIdAsync(ct);
        invoice.RecordPayment(paymentId, new Money(cmd.Amount), cmd.PaidOn, cmd.Method, cmd.Reference, new UserId(user.UserId),
            clock.Today, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return paymentId.Value;
    }
}

internal sealed record VoidPaymentCommand(long InvoiceId, long PaymentId, string Reason) : ICommand;

internal sealed class VoidPaymentValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VoidPaymentHandler(InvoicingDbContext db, IClock clock) : ICommandHandler<VoidPaymentCommand>
{
    public async ValueTask<Unit> Handle(VoidPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");
        invoice.VoidPayment(new PaymentId(cmd.PaymentId), cmd.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
