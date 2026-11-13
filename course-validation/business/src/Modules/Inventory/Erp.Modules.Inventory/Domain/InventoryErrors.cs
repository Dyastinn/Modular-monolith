using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Domain;

// Every business rule Inventory can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class InventoryErrors
{
    public static readonly ErrorCode FieldRequired = new("INVENTORY-0001", "A required field is empty",
        "Fill in the field named in the message.");
    public static readonly ErrorCode WholeUnitsOnly = new("INVENTORY-0002", "The item is counted in whole units",
        "Enter a whole number: this item's unit can't be split.");
    public static readonly ErrorCode AdjustmentMustChangeQuantity = new("INVENTORY-0003", "An adjustment must change the quantity",
        "Enter a positive number to add stock or a negative one to remove it.");
    public static readonly ErrorCode AdjustmentBelowReserved = new("INVENTORY-0004", "The adjustment would leave less than is reserved",
        "Stock reserved for orders can't be adjusted away. Cancel or ship those orders first, or adjust by less.");
    public static readonly ErrorCode ItemHasReservations = new("INVENTORY-0005", "The item has stock reserved for orders",
        "Ship or cancel those orders first, then deactivate the item.");
    public static readonly ErrorCode SkuTaken = new("INVENTORY-0006", "An item with that SKU already exists",
        "Choose another SKU, or edit the existing item.");
    public static readonly ErrorCode ShipmentNeedsLines = new("INVENTORY-0007", "A shipment needs at least one line",
        "The order has nothing left to ship.");
    public static readonly ErrorCode ReservationMissing = new("INVENTORY-0008", "The order's stock is no longer reserved",
        "Reload the order: it may have been cancelled or already shipped by someone else.");
    public static readonly ErrorCode SalesOrderNotFound = new("INVENTORY-0009", "The sales order doesn't exist",
        "Choose an approved order from the list of orders ready to ship.");
}
