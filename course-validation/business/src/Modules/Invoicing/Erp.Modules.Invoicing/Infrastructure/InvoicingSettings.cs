using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Invoicing.Infrastructure;

// What BIR RR 7-2024 Sec. 6(B) requires an invoice to show about the seller, read once at startup. A wrong value
// stops the app from starting instead of printing a non-compliant invoice.
internal sealed partial record InvoicingSettings(
    TaxPolicy Tax,
    string Title,
    string SellerName,
    string? TradeName,
    string? SellerTin,
    string? SellerAddress,
    string? PermitNumber,
    string? SeriesRange,
    string? PermitDateIssued)
{
    // "VAT Reg TIN 123-456-789-00000" or "Non-VAT Reg TIN ...": Sec. 6(B.2).
    public string TinStatement => $"{(Tax.Regime == TaxRegime.Vat ? "VAT" : "Non-VAT")} Reg TIN {SellerTin}";

    public static InvoicingSettings From(IConfiguration config)
    {
        var regimeText = config["Tax:Regime"] ?? nameof(TaxRegime.Vat);
        if (!Enum.TryParse<TaxRegime>(regimeText, ignoreCase: true, out var regime) || !Enum.IsDefined(regime))
            throw new InvalidOperationException($"Tax:Regime must be Vat, PercentageTax or Exempt, not '{regimeText}'.");

        var rateText = config["Tax:VatRate"];
        var rate = 0.12m;
        if (!string.IsNullOrWhiteSpace(rateText) && !decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out rate))
            throw new InvalidOperationException($"Tax:VatRate must be a number such as 0.12, not '{rateText}'.");

        TaxPolicy tax;
        try { tax = new TaxPolicy(regime, rate); }
        catch (DomainException e) { throw new InvalidOperationException($"Tax:VatRate: {e.Message}"); }

        var title = Text(config["Invoicing:Title"]) ?? "SALES INVOICE";
        if (!title.Contains("INVOICE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invoicing:Title must contain the word \"Invoice\" (RR 7-2024 Sec. 6(A.3)), not '{title}'.");

        var tin = Text(config["Company:Tin"]);
        if (tin is not null && !TinFormat().IsMatch(tin))
            throw new InvalidOperationException($"Company:Tin must be the TIN with its branch code, like 123-456-789-00000, not '{tin}'.");

        return new InvoicingSettings(tax, title.ToUpperInvariant(), Text(config["Company:Name"]) ?? "Your Company",
            Text(config["Company:TradeName"]), tin, Text(config["Company:Address"]),
            Text(config["Invoicing:Bir:PermitNumber"]), Text(config["Invoicing:Bir:SeriesRange"]), Text(config["Invoicing:Bir:DateIssued"]));
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^\d{3}-\d{3}-\d{3}-\d{5}$")]
    private static partial Regex TinFormat();
}
