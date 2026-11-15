using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

// Every business rule Invoicing can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class InvoicingErrors
{
    public static readonly ErrorCode CompanyFieldRequired = new("INVOICING-0001", "A company detail is missing",
        "Fill in the field named in the message under Invoicing → Company and tax.");
    public static readonly ErrorCode InvalidTin = new("INVOICING-0002", "The TIN is not in the BIR format",
        "Enter it with the branch code, like 123-456-789-00000.");
    public static readonly ErrorCode InvoiceTitleMissingWord = new("INVOICING-0003", "The invoice title must contain \"Invoice\"",
        "BIR RR 7-2024 Sec. 6(A.3) requires it: for example \"Sales Invoice\".");
    public static readonly ErrorCode InvalidPermitDate = new("INVOICING-0004", "The permit date is not in the BIR format",
        "Write it mm/dd/yyyy, exactly as on the BIR permit.");
    public static readonly ErrorCode CompanyProfileIncomplete = new("INVOICING-0005", "The company profile is incomplete",
        "Nothing can be shipped until an invoice can be printed. Fill in the registered name, TIN and address under Invoicing → Company and tax.");
    public static readonly ErrorCode InvoiceNeedsLines = new("INVOICING-0006", "An invoice needs at least one line",
        "The shipment had nothing to invoice. Check the shipment.");
    public static readonly ErrorCode InvoiceIsVoid = new("INVOICING-0007", "The invoice is void",
        "A void invoice can't take payments. Use the invoice that replaced it.");
    public static readonly ErrorCode VoidReasonRequired = new("INVOICING-0008", "Voiding needs a reason",
        "Write why it's being voided. The reason is kept for the audit trail.");
    public static readonly ErrorCode OnlyVoidInvoiceReissued = new("INVOICING-0009", "Only a void invoice can be reissued",
        "Void the invoice first, with the reason, then reissue it.");
    public static readonly ErrorCode PaymentNotPositive = new("INVOICING-0010", "A payment must be more than zero",
        "Enter the amount received, plus any tax withheld.");
    public static readonly ErrorCode PaymentExceedsBalance = new("INVOICING-0011", "The payment is more than the balance due",
        "Enter at most the balance due. Record any overpayment separately with the accountant.");
    public static readonly ErrorCode WithholdingExceedsNetSales = new("INVOICING-0012", "More tax withheld than the sales allow",
        "Tax withheld is a percentage of the sales before VAT. Check the amount on the customer's BIR Form 2307.");
    public static readonly ErrorCode PaymentBeforeInvoice = new("INVOICING-0013", "A payment can't be dated before the invoice",
        "Enter the date the payment was actually received.");
    public static readonly ErrorCode PaymentInFuture = new("INVOICING-0014", "A payment can't be dated in the future",
        "Enter the date the payment was actually received.");
    public static readonly ErrorCode PaymentNotOnInvoice = new("INVOICING-0015", "That payment isn't on this invoice",
        "Reload the invoice: its payments may have changed.");
    public static readonly ErrorCode PaymentAlreadyVoid = new("INVOICING-0016", "The payment is already void",
        "Nothing to do. Record the payment again if it was voided by mistake.");
    public static readonly ErrorCode InvoiceHasPayments = new("INVOICING-0017", "The invoice has payments",
        "Void its payments first, then void the invoice.");
    public static readonly ErrorCode InvalidQuarter = new("INVOICING-0018", "Invalid quarter",
        "Enter 1, 2, 3 or 4.");
    public static readonly ErrorCode InvalidCertificateYear = new("INVOICING-0019", "Invalid year for a certificate",
        "Enter the year printed on the BIR Form 2307; it can't be in the future.");
    public static readonly ErrorCode CertificateAmountZero = new("INVOICING-0020", "The certificate shows no tax withheld",
        "Enter the total tax withheld shown on the BIR Form 2307.");
    public static readonly ErrorCode CertificateReceivedInFuture = new("INVOICING-0021", "A certificate can't be received in the future",
        "Enter the date the BIR Form 2307 actually arrived.");
    public static readonly ErrorCode CertificateAlreadyVoid = new("INVOICING-0022", "The certificate is already void",
        "Nothing to do. Record a new certificate if the old one was replaced.");
    public static readonly ErrorCode CustomerNeverInvoiced = new("INVOICING-0023", "The customer has no invoices",
        "A customer we never invoiced can't have withheld tax from us. Check the customer.");
    public static readonly ErrorCode OpeningBalanceNotPositive = new("INVOICING-0024", "An opening balance must be more than zero",
        "Leave out customers who owed nothing on the day the system went live.");
    public static readonly ErrorCode OpeningBalanceInFuture = new("INVOICING-0025", "An opening balance can't be dated in the future",
        "Use the date of the customer's original invoice.");
    public static readonly ErrorCode DueBeforeIssue = new("INVOICING-0026", "The due date is before the invoice date",
        "Check the two dates; leave the due date out to use the customer's payment terms.");
    public static readonly ErrorCode OpeningBalanceReferenceRequired = new("INVOICING-0027", "An opening balance needs the old invoice's number",
        "Enter the number of the invoice it comes from, so payments can be matched to it.");
    public static readonly ErrorCode OpeningBalanceNotReissued = new("INVOICING-0028", "An opening balance isn't reissued",
        "Void it if it was wrong, and record the right amount as a new opening balance.");
    public static readonly ErrorCode CustomerNotFound = new("INVOICING-0029", "There is no customer with that name",
        "Import the customers first, or correct the name to match the Customers page exactly.");
    public static readonly ErrorCode DuplicateOpeningBalance = new("INVOICING-0030", "That opening balance is already recorded",
        "The same customer and old invoice number appear twice, or were imported before. Leave the row out.");
    public static readonly ErrorCode InvoiceHasCredits = new("INVOICING-0031", "The invoice has credit memos",
        "An invoice with credit memos stays: credit the rest of it instead of voiding it.");
    public static readonly ErrorCode OpeningBalanceNotCredited = new("INVOICING-0032", "An opening balance isn't credited",
        "Void the opening balance and record the right amount as a new one.");
    public static readonly ErrorCode CreditReasonRequired = new("INVOICING-0033", "A credit memo needs a reason",
        "Say why: goods returned, wrong price, damaged in delivery.");
    public static readonly ErrorCode NothingToCredit = new("INVOICING-0034", "Nothing to credit",
        "Enter a quantity on at least one line.");
    public static readonly ErrorCode LineNotOnInvoice = new("INVOICING-0035", "That line isn't on the invoice",
        "Reload the invoice and choose its lines again.");
    public static readonly ErrorCode CreditExceedsInvoiced = new("INVOICING-0036", "More would be credited than was billed",
        "Credit at most what is left on each line; earlier credit memos count.");
    public static readonly ErrorCode DiscountedInvoiceCreditedWhole = new("INVOICING-0037", "An invoice with a senior citizen or PWD discount is credited whole",
        "Credit every line in full, then invoice again what the customer keeps.");
    public static readonly ErrorCode NoRefundDue = new("INVOICING-0038", "There is no refund due",
        "The whole credit was taken off what the invoice still owed.");
    public static readonly ErrorCode RefundAlreadyRecorded = new("INVOICING-0039", "The refund is already recorded",
        "Nothing more to do. If it was recorded by mistake, tell an admin.");
}
