namespace Erp.Modules.Sales.Domain;

// A REFERENCE to an item the Sales module doesn't own (items live in the catalog / Inventory side).
// Sales only ever names an item by its id, so the id is shared vocabulary in Common/ — not part of
// any Sales aggregate's folder.
public readonly record struct ItemId(long Value);
