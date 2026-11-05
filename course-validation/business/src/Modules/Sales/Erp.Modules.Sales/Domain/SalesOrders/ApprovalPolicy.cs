namespace Erp.Modules.Sales.Domain;

internal static class ApprovalPolicy
{
    // Orders above this need a manager; at or below it they are approved on submit.
    public static readonly Money Threshold = new(50_000m);
}
