using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Features;

internal static class InvoicingRoles
{
    public static readonly string[] Accountants = [Roles.Accountant, Roles.Admin];
    public static readonly string[] Readers = [Roles.Accountant, Roles.Admin, Roles.SalesManager];
}
