using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Contracts;

// Sales lowers the customer's balance when money comes in, and raises it again if the payment is voided.
// Delivered at least once: handlers must apply each PaymentId once.
public sealed record PaymentRecorded(long PaymentId, long CustomerId, decimal Amount) : IIntegrationEvent;
public sealed record PaymentVoided(long PaymentId, long CustomerId, decimal Amount) : IIntegrationEvent;
