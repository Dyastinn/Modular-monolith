using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Infrastructure;

// Inventory's domain events, told to the other modules in the plain words of Inventory.Contracts. The domain can be
// renamed or reshaped freely; only these two mappings must keep the published shape.
internal sealed class PublishItemCatalogChanged(IIntegrationEventPublisher publisher) : IDomainEventHandler<ItemChanged>
{
    public Task HandleAsync(ItemChanged e, CancellationToken ct) =>
        publisher.PublishAsync(new ItemCatalogChanged(e.ItemId.Value, e.Sku, e.Name, e.Unit, e.AllowsFractions, e.IsActive), ct);
}

internal sealed class PublishShipmentRecorded(IIntegrationEventPublisher publisher) : IDomainEventHandler<ShipmentCreated>
{
    public Task HandleAsync(ShipmentCreated e, CancellationToken ct) =>
        publisher.PublishAsync(new ShipmentRecorded(e.ShipmentId.Value, e.Number, e.SalesOrderId, e.ShippedUtc), ct);
}
