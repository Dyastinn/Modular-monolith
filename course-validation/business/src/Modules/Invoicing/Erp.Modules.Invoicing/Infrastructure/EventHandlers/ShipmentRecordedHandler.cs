using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Infrastructure;

// Goods left the building: bill them. Idempotent because the issuer returns the existing invoice on a repeat.
internal sealed class ShipmentRecordedHandler(InvoiceIssuer issuer) : IIntegrationEventHandler<ShipmentRecorded>
{
    public async Task HandleAsync(ShipmentRecorded e, CancellationToken ct) =>
        await issuer.IssueForShipmentAsync(e.ShipmentId, e.SalesOrderId, ct);
}
