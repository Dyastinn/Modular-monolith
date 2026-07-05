using Erp.Shared.BuildingBlocks;
using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Domain;

// Events implement BOTH markers: IDomainEvent (so the aggregate records them) and INotification
// (so IPublisher can dispatch them). See Phase 7 §3.
public sealed record SalesOrderCreated(CustomerId CustomerId) : IDomainEvent, INotification;
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent, INotification;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent, INotification;
