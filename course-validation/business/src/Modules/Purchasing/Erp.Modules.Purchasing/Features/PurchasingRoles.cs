using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Features;

internal static class PurchasingRoles
{
    public static readonly string[] Buyers = [Roles.Purchaser, Roles.Admin];
    public static readonly string[] Approvers = [Roles.Admin];
    public static readonly string[] Receivers = [Roles.InventoryClerk, Roles.Purchaser, Roles.Admin];
    public static readonly string[] Readers = [Roles.Purchaser, Roles.Admin, Roles.InventoryClerk, Roles.Accountant];
}
