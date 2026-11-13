using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Every business rule Sales can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class SalesErrors
{
    public static readonly ErrorCode CustomerNameRequired = new("SALES-0001", "A customer needs a name",
        "Enter the customer's registered or trade name.");
    public static readonly ErrorCode CustomerBlocked = new("SALES-0002", "The customer is blocked",
        "A blocked customer can't place orders. A sales manager can unblock them on the Customers page once the reason is settled.");
    public static readonly ErrorCode CreditLimitExceeded = new("SALES-0003", "The order would exceed the credit limit",
        "Reduce the order, wait for the customer's payments, or ask a sales manager to raise the credit limit. Orders awaiting approval count against the limit too.");
    public static readonly ErrorCode CategoryNameRequired = new("SALES-0004", "A category needs a name",
        "Enter a name for the category.");
    public static readonly ErrorCode CategoryMaxQuantityInvalid = new("SALES-0005", "Invalid maximum quantity per order",
        "Enter a maximum greater than zero.");
    public static readonly ErrorCode UnknownRole = new("SALES-0006", "Unknown role",
        "Choose one of the roles in the list.");
    public static readonly ErrorCode CategoryRoleRequired = new("SALES-0007", "Your role can't order items in this category",
        "Ask someone with the category's required role to place the order, or ask an admin to change the category.");
    public static readonly ErrorCode CategoryQuantityExceeded = new("SALES-0008", "Too much of one category on one order",
        "Lower the quantity, or ask an admin to raise the category's maximum per order.");
    public static readonly ErrorCode PriceRequiredForSale = new("SALES-0009", "The item has no price",
        "Set a price above zero before putting the item on sale.");
    public static readonly ErrorCode InactiveItemNotSellable = new("SALES-0010", "The item is inactive in the catalog",
        "Reactivate the item under Inventory → Items first.");
    public static readonly ErrorCode ItemNotForSale = new("SALES-0011", "The item is not for sale",
        "Choose another item, or put this one on sale in the price list.");
    public static readonly ErrorCode WholeUnitsOnly = new("SALES-0012", "The item is sold in whole units",
        "Enter a whole number: this item's unit can't be split.");
    public static readonly ErrorCode OrderNeedsLines = new("SALES-0013", "The order has no lines",
        "Add at least one item to the order.");
    public static readonly ErrorCode CreatorCannotApprove = new("SALES-0014", "You can't approve your own order",
        "Another sales manager must approve it.");
    public static readonly ErrorCode RejectionReasonRequired = new("SALES-0015", "A rejection needs a reason",
        "Write why the order is rejected, so the sales rep can fix it.");
    public static readonly ErrorCode LinesOnlyOnDraft = new("SALES-0016", "Lines can only change on a draft order",
        "Cancel the order and create a new one if the lines must change.");
    public static readonly ErrorCode InvalidStatusChange = new("SALES-0017", "The order can't move to that status",
        "Reload the order: someone may have changed it. The status shown lists the actions still possible.");
    public static readonly ErrorCode CustomerNotFound = new("SALES-0018", "The customer doesn't exist",
        "Choose a customer from the list. If you just created one, reload the page.");
    public static readonly ErrorCode ItemNotFound = new("SALES-0019", "An item on the order doesn't exist",
        "Choose items from the list. A new catalog item appears in Sales a few seconds after it's created.");
    public static readonly ErrorCode InsufficientStock = new("SALES-0020", "Not enough stock",
        "Lower the quantity or wait for the next delivery. The order stays a draft.");
    public static readonly ErrorCode PaymentTermsOutOfRange = new("SALES-0021", "Payment terms out of range",
        "Enter payment terms between 0 (cash) and 365 days.");
    public static readonly ErrorCode CategoryNameTaken = new("SALES-0022", "A category with that name already exists",
        "Choose another name, or edit the existing category.");
    public static readonly ErrorCode CategoryNotFound = new("SALES-0023", "The category doesn't exist",
        "Choose a category from the list.");
    public static readonly ErrorCode OnlyApprovedOrdersShip = new("SALES-0024", "Only approved orders can be shipped",
        "Submit the order and get it approved first.");
    public static readonly ErrorCode UnknownTaxType = new("SALES-0025", "Unknown tax type",
        "Choose VATable, zero-rated or VAT-exempt.");
    public static readonly ErrorCode DiscountProgramEnded = new("SALES-0026", "That discount program is no longer offered",
        "Choose an active program, or ask an admin to reactivate it under Price list → Special discounts.");
    public static readonly ErrorCode NoLineQualifiesForDiscount = new("SALES-0027", "No item on the order qualifies for the discount",
        "Only items marked eligible in the price list get a senior citizen or PWD discount.");
    public static readonly ErrorCode DiscountOnlyOnDraft = new("SALES-0028", "The discount can only change on a draft order",
        "Cancel the order and create a new one if the discount must change.");
    public static readonly ErrorCode DiscountProgramNotFound = new("SALES-0029", "There is no such discount program",
        "Choose a program from the list.");
}
