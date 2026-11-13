using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Infrastructure;

// Invoicing's payment events, told to the other modules in the plain words of Invoicing.Contracts.
internal sealed class PublishPaymentRecorded(IIntegrationEventPublisher publisher) : IDomainEventHandler<InvoicePaymentRecorded>
{
    public Task HandleAsync(InvoicePaymentRecorded e, CancellationToken ct) =>
        publisher.PublishAsync(new PaymentRecorded(e.PaymentId.Value, e.CustomerId, e.Settled.Amount), ct);
}

internal sealed class PublishPaymentVoided(IIntegrationEventPublisher publisher) : IDomainEventHandler<InvoicePaymentVoided>
{
    public Task HandleAsync(InvoicePaymentVoided e, CancellationToken ct) =>
        publisher.PublishAsync(new PaymentVoided(e.PaymentId.Value, e.CustomerId, e.Settled.Amount), ct);
}
