using System.Text.RegularExpressions;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

// Who the seller is for the BIR (RR 7-2024 Sec. 6(B)) and how it's taxed. One row, kept by the business on a screen:
// the law changes the rate, the BIR issues the permit, and neither should need a developer.
internal sealed partial class CompanyProfile
{
    public const int SingletonId = 1;

    public int Id { get; private set; }
    public string RegisteredName { get; private set; } = "";
    public string? TradeName { get; private set; }
    public string Tin { get; private set; } = "";
    public string RegisteredAddress { get; private set; } = "";
    public TaxRegime TaxRegime { get; private set; }
    public decimal VatRate { get; private set; }
    public string InvoiceTitle { get; private set; } = "SALES INVOICE";
    public string? PermitNumber { get; private set; }      // PTU or Acknowledgement Certificate Control No., once registered
    public string? SeriesRange { get; private set; }
    public string? PermitDateIssued { get; private set; }  // mm/dd/yyyy, as the BIR prints it
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedUtc { get; private set; }

    public TaxPolicy Tax => new(TaxRegime, VatRate);
    public bool IsComplete => RegisteredName.Length > 0 && Tin.Length > 0 && RegisteredAddress.Length > 0;

    private CompanyProfile() { }

    public static CompanyProfile Blank() =>
        new() { Id = SingletonId, TaxRegime = TaxRegime.Vat, VatRate = 0.12m, InvoiceTitle = "SALES INVOICE" };

    public void Update(string registeredName, string? tradeName, string tin, string registeredAddress, TaxPolicy tax,
        string invoiceTitle, string? permitNumber, string? seriesRange, string? permitDateIssued, UserId by, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(registeredName)) throw new DomainException("The registered name is required.");
        if (string.IsNullOrWhiteSpace(registeredAddress)) throw new DomainException("The registered address is required.");
        if (!TinFormat().IsMatch(tin.Trim()))
            throw new DomainException("The TIN must include its branch code, like 123-456-789-00000.");
        if (!invoiceTitle.Contains("INVOICE", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("The invoice title must contain the word \"Invoice\" (RR 7-2024 Sec. 6(A.3)).");
        if (Text(permitDateIssued) is { } date && !PermitDateFormat().IsMatch(date))
            throw new DomainException("The permit's date issued is written mm/dd/yyyy, as on the BIR permit.");

        RegisteredName = registeredName.Trim();
        TradeName = Text(tradeName);
        Tin = tin.Trim();
        RegisteredAddress = registeredAddress.Trim();
        (TaxRegime, VatRate) = (tax.Regime, tax.VatRate);
        InvoiceTitle = invoiceTitle.Trim().ToUpperInvariant();
        (PermitNumber, SeriesRange, PermitDateIssued) = (Text(permitNumber), Text(seriesRange), Text(permitDateIssued));
        (UpdatedBy, UpdatedUtc) = (by, nowUtc);
    }

    // Goods shouldn't leave without an invoice, and an invoice without the seller's registered details isn't one.
    public void EnsureReadyToInvoice()
    {
        if (!IsComplete)
            throw new DomainException("Fill in the company's registered name, TIN and address (Invoicing → Company and tax) before shipping.");
    }

    public SellerDetails Seller() =>
        new(RegisteredName, TradeName, Tin, RegisteredAddress, InvoiceTitle, PermitNumber, SeriesRange, PermitDateIssued);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^\d{3}-\d{3}-\d{3}-\d{5}$")]
    private static partial Regex TinFormat();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex PermitDateFormat();
}

// The seller as printed on one invoice, copied at issue: a reprint shows what the original showed, even after the
// company moves or gets a new permit.
internal sealed record SellerDetails(string Name, string? TradeName, string Tin, string Address, string InvoiceTitle,
    string? PermitNumber, string? SeriesRange, string? PermitDateIssued)
{
    private SellerDetails() : this("", null, "", "", "", null, null, null) { }
}
