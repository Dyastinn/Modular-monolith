namespace Erp.Modules.Sales.Domain;

// The SalesOrder aggregate owns its own identity type, so it lives in the aggregate's folder.
internal readonly record struct SalesOrderId(long Value);
