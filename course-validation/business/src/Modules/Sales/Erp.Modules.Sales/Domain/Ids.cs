namespace Erp.Modules.Sales.Domain;

internal readonly record struct SalesOrderId(long Value);
internal readonly record struct CustomerId(long Value);
internal readonly record struct CategoryId(long Value);

// Owned by Inventory; Sales keeps a priced copy under the same id.
internal readonly record struct ItemId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct UserId(long Value);
