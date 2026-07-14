namespace Erp.Modules.Sales.Domain;

// Strongly-typed IDs (Phase 5 §3a). Cross-cutting primitives — shared by every aggregate, so they
// live in Common/ rather than in any one aggregate's folder.
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ItemId(long Value);
public readonly record struct UserId(long Value);
