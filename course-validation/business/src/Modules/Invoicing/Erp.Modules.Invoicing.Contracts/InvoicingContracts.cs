using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Contracts;

// Sales lowers the customer's balance when money comes in, and raises it again if the payment is voided.
// Delivered at least once: handlers must apply each PaymentId once.
public sealed record PaymentRecorded(long PaymentId, long CustomerId, decimal Amount) : IIntegrationEvent;

// What a customer owed before the system went live. Sales adds it to their balance, and takes it off again if it is
// voided, so the credit check sees the old debt too.
// Number: the document's OB-… number, unique, which also makes a repeated delivery recognisable.
public sealed record OpeningBalanceCharged(string Number, long CustomerId, decimal Amount) : IIntegrationEvent;
public sealed record OpeningBalanceVoided(string Number, long CustomerId, decimal Amount) : IIntegrationEvent;

// A credit memo took AppliedToInvoice off what the customer owed (any part they had already paid is refunded to them,
// so it changes nothing they owe). Number: the CM-… document, unique, so a repeat delivery is recognisable.
public sealed record CreditMemoIssued(string Number, long CustomerId, decimal AppliedToInvoice) : IIntegrationEvent;
public sealed record PaymentVoided(long PaymentId, long CustomerId, decimal Amount) : IIntegrationEvent;
