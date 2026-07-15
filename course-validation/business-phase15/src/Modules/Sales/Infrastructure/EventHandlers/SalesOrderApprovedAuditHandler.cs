using Erp.Modules.Sales.Domain;
using Erp.Shared.Messaging;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Infrastructure;

// An after-commit reaction (Phase 8 §6). Registered by the module's assembly scan.
public sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : IDomainEventHandler<SalesOrderApproved>
{
    public Task Handle(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Order {Id} approved by user {Approver} for {Total:C}",
            e.Id.Value, e.Approver.Value, e.Total.Amount);
        return Task.CompletedTask;
    }
}
