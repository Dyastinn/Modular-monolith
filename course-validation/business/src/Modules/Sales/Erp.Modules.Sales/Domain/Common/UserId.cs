namespace Erp.Modules.Sales.Domain;

// A REFERENCE to a user owned by the Identity module. Sales names the creator/approver by id only,
// so this shared id lives in Common/.
internal readonly record struct UserId(long Value);
