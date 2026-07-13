namespace Erp.Modules.Sales.Domain;

// Strongly-typed IDs (Phase 5 §3a). Grouped here because they're the same one-line pattern;
// split into one file each if you prefer — the lesson leaves that to you.
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ItemId(long Value);
public readonly record struct UserId(long Value);
