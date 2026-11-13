using System.Globalization;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Infrastructure;

// Delivered by the outbox, so it can run more than once for the same approval. Logging twice is harmless.
internal sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : IDomainEventHandler<SalesOrderApproved>
{
    public Task HandleAsync(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Sales order {OrderId} approved by {Approver} for ₱{Total:N2}",
            e.Id.Value, e.ApprovedBy?.Value.ToString(CultureInfo.InvariantCulture) ?? "auto-approval", e.Total.Amount);
        return Task.CompletedTask;
    }
}
