using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Domain events: facts the order records so other code can react after the save commits.
// Only events that something actually handles are raised. An event nobody listens to is dead code — and the
// Mediator generator's MSG0005 warning ("message without any registered handler") stays switched on so it
// can point out exactly that. Add an event in the same change that adds its first handler.
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
