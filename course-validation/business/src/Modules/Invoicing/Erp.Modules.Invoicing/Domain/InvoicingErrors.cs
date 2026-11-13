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
}
