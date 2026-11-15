using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Sales' business values, edited on the Approval rules page. One row.
internal sealed class SalesSettings
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    // Orders above this wait for a sales manager, unless the customer has a limit of its own.
    public Money ApprovalThreshold { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedUtc { get; private set; }

    private SalesSettings() { }

    public static SalesSettings Default(Money approvalThreshold) => new() { ApprovalThreshold = approvalThreshold };

    public void SetApprovalThreshold(Money threshold, UserId by, DateTime nowUtc) =>
        (ApprovalThreshold, UpdatedBy, UpdatedUtc) = (threshold, by, nowUtc);
}
