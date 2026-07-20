using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Domain events (Phase 5 §8). IDomainEvent lives in BuildingBlocks; we just implement it.
internal sealed record SalesOrderCreated(SalesOrderId Id, CustomerId CustomerId, UserId CreatedBy) : IDomainEvent;
internal sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
internal sealed record SalesOrderRejected(SalesOrderId Id) : IDomainEvent;
internal sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;
