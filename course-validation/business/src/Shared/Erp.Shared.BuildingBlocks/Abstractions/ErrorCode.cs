using System.Reflection;

namespace Erp.Shared.BuildingBlocks;

// One kind of failure with a stable ID, like the compiler's CS0122: the code names the problem, the catalog says how
// to fix it. A published code never changes meaning, and its number is never reused.
public sealed record ErrorCode(string Code, string Title, string Fix);

// The codes one module can answer with. Each module registers its own; the host serves them all at /api/errors.
public sealed class ErrorCatalog(IReadOnlyList<ErrorCode> codes)
{
    public IReadOnlyList<ErrorCode> Codes { get; } = codes;

    // Every public static ErrorCode field of a catalog class such as SalesErrors.
    public static ErrorCatalog From(Type catalogClass) =>
        new(catalogClass.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ErrorCode))
            .Select(f => (ErrorCode)f.GetValue(null)!)
            .ToList());
}

// Codes that belong to no module: the HTTP layer, and the value objects every module shares.
public static class CommonErrors
{
    public static readonly ErrorCode ValidationFailed = new("ERP-0001", "The request has invalid fields",
        "Correct the fields listed in the message and send it again.");
    public static readonly ErrorCode NotFound = new("ERP-0002", "Not found",
        "Check the number or link. If it was created a moment ago on another screen, such as a new catalog item, wait a few seconds and reload.");
    public static readonly ErrorCode ChangedBySomeoneElse = new("ERP-0003", "Someone else changed this first",
        "Reload to see their change, then make yours again.");
    public static readonly ErrorCode UnreadableRequest = new("ERP-0004", "The request could not be read",
        "The request body is missing or isn't valid JSON. If you're calling the API directly, check the body and the Content-Type header.");
    public static readonly ErrorCode NotSignedIn = new("ERP-0005", "Not signed in",
        "Sign in again. Sessions end after a period without use, or when an admin signs you out everywhere.");
    public static readonly ErrorCode NotAllowed = new("ERP-0006", "Not allowed",
        "Your role can't do this. Ask an admin if you need it.");
    public static readonly ErrorCode TooManyRequests = new("ERP-0007", "Too many requests",
        "Wait a minute and try again. Repeated failed sign-ins are limited on purpose.");
    public static readonly ErrorCode Unexpected = new("ERP-0008", "Unexpected error",
        "Try again. If it keeps happening, send the trace id from the message to support: it finds this exact failure in the logs.");
    public static readonly ErrorCode AlreadyExists = new("ERP-0009", "That already exists",
        "Someone created the same thing at the same moment. Reload and use the existing one.");

    public static readonly ErrorCode NegativeMoney = new("ERP-0101", "An amount can't be negative",
        "Enter zero or a positive amount.");
    public static readonly ErrorCode QuantityNotPositive = new("ERP-0102", "Quantity must be greater than zero",
        "Enter a quantity above zero.");
    public static readonly ErrorCode QuantityTooPrecise = new("ERP-0103", "Too many decimal places in a quantity",
        "Quantities allow at most three decimal places, such as 2.125.");
    public static readonly ErrorCode UnknownTaxRegime = new("ERP-0104", "Unknown tax regime",
        "Choose VAT, percentage tax or exempt.");
    public static readonly ErrorCode InvalidVatRate = new("ERP-0105", "Invalid VAT rate",
        "Enter the rate as a fraction between 0 and 1: 0.12 for 12%.");
    public static readonly ErrorCode SpecialDiscountHolderMissing = new("ERP-0106", "The discount holder's details are missing",
        "A senior citizen or PWD discount needs the holder's name and ID number, exactly as on the ID card.");
    public static readonly ErrorCode InvalidDiscountRate = new("ERP-0107", "Invalid discount rate",
        "Enter the rate as a fraction between 0 and 1: 0.20 for 20%.");
}
