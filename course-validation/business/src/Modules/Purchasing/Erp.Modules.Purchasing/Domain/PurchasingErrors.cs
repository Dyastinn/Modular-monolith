using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Domain;

// Every business rule Purchasing can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class PurchasingErrors
{
    public static readonly ErrorCode ReceiptExceedsOutstanding = new("PURCHASING-0001", "More was received than is still to come",
        "Enter only what arrived for this line. Extra goods need a new purchase order.");
    public static readonly ErrorCode OrderNeedsLines = new("PURCHASING-0002", "The purchase order has no lines",
        "Add at least one item.");
    public static readonly ErrorCode DuplicateItem = new("PURCHASING-0003", "An item appears twice",
        "Put each item on one line and add up the quantities.");
    public static readonly ErrorCode InactiveItem = new("PURCHASING-0004", "The item is inactive",
        "Reactivate the item under Inventory → Items, or choose another.");
    public static readonly ErrorCode WholeUnitsOnly = new("PURCHASING-0005", "The item is counted in whole units",
        "Enter a whole number: this item's unit can't be split.");
    public static readonly ErrorCode CreatorCannotApprove = new("PURCHASING-0006", "You can't approve your own purchase order",
        "Another admin must approve it.");
    public static readonly ErrorCode NothingReceived = new("PURCHASING-0007", "No received quantity was entered",
        "Enter the quantity that arrived on at least one line.");
    public static readonly ErrorCode LineNotOnOrder = new("PURCHASING-0008", "That line isn't on this purchase order",
        "Reload the purchase order: its lines may have changed.");
    public static readonly ErrorCode InvalidStatusChange = new("PURCHASING-0009", "The purchase order can't do that in its current status",
        "Reload it: someone may have changed it. The status shown lists the actions still possible.");
    public static readonly ErrorCode SupplierNameRequired = new("PURCHASING-0010", "A supplier needs a name",
        "Enter the supplier's registered or trade name.");
    public static readonly ErrorCode PaymentTermsOutOfRange = new("PURCHASING-0011", "Payment terms out of range",
        "Enter payment terms between 0 (cash) and 365 days.");
    public static readonly ErrorCode SupplierInactive = new("PURCHASING-0012", "The supplier is inactive",
        "Reactivate the supplier, or choose another.");
    public static readonly ErrorCode SupplierNotFound = new("PURCHASING-0013", "The supplier doesn't exist",
        "Choose a supplier from the list.");
    public static readonly ErrorCode ItemNotFound = new("PURCHASING-0014", "An item on the purchase order doesn't exist",
        "Choose items from the list.");
}
