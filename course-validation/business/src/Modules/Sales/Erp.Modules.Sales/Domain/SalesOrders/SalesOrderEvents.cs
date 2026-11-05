using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Raise an event only in the change that adds its first handler.
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId? ApprovedBy) : IDomainEvent;
