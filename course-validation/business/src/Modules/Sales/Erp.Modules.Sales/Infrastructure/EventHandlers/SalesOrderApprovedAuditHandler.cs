using Erp.Modules.Sales.Domain;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Infrastructure;

// Delivered by the outbox, so it can run more than once for the same approval. Logging twice is harmless.
internal sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : INotificationHandler<SalesOrderApproved>
{
    public ValueTask Handle(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Sales order {OrderId} approved by {Approver} for ₱{Total:N2}",
            e.Id.Value, e.ApprovedBy?.Value.ToString() ?? "auto-approval", e.Total.Amount);
        return ValueTask.CompletedTask;
    }
}
