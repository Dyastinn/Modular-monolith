namespace Erp.Modules.Sales.Domain;

// A data-access "port" the module declares; Sales's own Infrastructure implements it (Phase 6 §4).
public interface IItemRepository
{
    // Loads the requested items WITH their category rules, in one round-trip.
    Task<IReadOnlyList<ItemForOrder>> GetForOrderAsync(IReadOnlyList<long> itemIds, CancellationToken ct);
}

// A small read shape the handler needs: item + its price + its category rules.
public sealed record ItemForOrder(long ItemId, bool IsActive, Money UnitPrice, ItemCategory Category);
