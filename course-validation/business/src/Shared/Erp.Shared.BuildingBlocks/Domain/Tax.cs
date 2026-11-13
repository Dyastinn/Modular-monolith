namespace Erp.Shared.BuildingBlocks;

// How a sale of an item is treated for VAT (BIR RR 7-2024 Sec. 3(B)). Stored as numbers: never renumber.
public enum TaxType
{
    Vatable = 0,
    ZeroRated = 1,
    VatExempt = 2
}

// How the seller is registered with the BIR. Stored as numbers: never renumber.
public enum TaxRegime
{
    Vat = 0,             // VAT-registered: prices include VAT, invoices show the VAT breakdown
    PercentageTax = 1,   // non-VAT, subject to percentage tax: invoices show sales subject to percentage tax
    Exempt = 2           // non-VAT, subject to neither: invoices say "EXEMPT"
}

// The seller's tax setup, kept in Invoicing's company profile. Copied onto every invoice and every special discount.
public sealed record TaxPolicy
{
    public TaxRegime Regime { get; }
    public decimal VatRate { get; }

    public TaxPolicy(TaxRegime regime, decimal vatRate)
    {
        if (!Enum.IsDefined(regime)) throw new DomainException(CommonErrors.UnknownTaxRegime, $"'{regime}' is not a tax regime.");
        if (vatRate is < 0 or >= 1) throw new DomainException(CommonErrors.InvalidVatRate, "The VAT rate must be a fraction between 0 and 1, such as 0.12.");
        Regime = regime;
        VatRate = regime == TaxRegime.Vat ? vatRate : 0;
    }
}
