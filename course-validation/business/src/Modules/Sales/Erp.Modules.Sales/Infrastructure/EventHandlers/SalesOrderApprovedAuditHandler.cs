using Erp.Modules.Sales.Domain;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Infrastructure;

// Reacts after an approval has committed. The Mediator source generator wires it up — no registration needed.
internal sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : INotificationHandler<SalesOrderApproved>
{
    public ValueTask Handle(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Sales order {OrderId} approved by user {ApproverId} for ₱{Total:N2}",
            e.Id.Value, e.Approver.Value, e.Total.Amount);
        return ValueTask.CompletedTask;
    }
}
