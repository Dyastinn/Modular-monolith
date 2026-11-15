using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Domain;

// Purchasing's business values, edited on the Approval rules page. One row.
internal sealed class PurchasingSettings
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    // Purchase orders above this wait for an admin. ₱0: every order does.
    public Money ApprovalThreshold { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedUtc { get; private set; }

    private PurchasingSettings() { }

    public static PurchasingSettings Default(Money approvalThreshold) => new() { ApprovalThreshold = approvalThreshold };

    public void SetApprovalThreshold(Money threshold, UserId by, DateTime nowUtc) =>
        (ApprovalThreshold, UpdatedBy, UpdatedUtc) = (threshold, by, nowUtc);
}
