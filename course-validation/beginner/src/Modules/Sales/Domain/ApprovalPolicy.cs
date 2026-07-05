namespace Erp.Modules.Sales.Domain;

// NOTE: referenced by SalesOrder.RequiresApproval but no code given in Phase 3. Inferred.
public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(50000m);
}
