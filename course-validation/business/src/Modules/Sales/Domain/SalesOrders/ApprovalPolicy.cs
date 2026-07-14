namespace Erp.Modules.Sales.Domain;

public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(50_000m);  // orders over ₱50,000 are flagged high-value
}
