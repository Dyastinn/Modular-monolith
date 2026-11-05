using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

// Who may do what in Sales, decided once for every endpoint.
internal static class SalesRoles
{
    public static readonly string[] Sellers = [Roles.SalesRep, Roles.SalesManager, Roles.Admin];
    public static readonly string[] Managers = [Roles.SalesManager, Roles.Admin];

    // Customers and orders are also read by the people who ship and bill them.
    public static readonly string[] Readers =
        [Roles.SalesRep, Roles.SalesManager, Roles.Admin, Roles.InventoryClerk, Roles.Accountant];
}
