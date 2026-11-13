namespace Erp.Shared.BuildingBlocks;

// A discount the law gives a person rather than a customer account: senior citizens, PWDs and the others in BIR RR 7-2024
// Sec. 6(B.18). The order and the invoice both keep a copy, including the VAT rate it removed, so their totals always agree.
public sealed record SpecialDiscount
{
    public string Program { get; private set; } = default!;     // "Senior citizen"
    public string IdLabel { get; private set; } = default!;     // "OSCA ID No."
    public string HolderName { get; private set; } = default!;
    public string IdNumber { get; private set; } = default!;
    public decimal Rate { get; private set; }                   // 0.20
    public decimal VatRateRemoved { get; private set; }         // the VAT rate when the sale is VAT-exempt for this person, else 0

    private SpecialDiscount() { }

    public SpecialDiscount(string program, string idLabel, string holderName, string idNumber, decimal rate, decimal vatRateRemoved)
    {
        if (string.IsNullOrWhiteSpace(holderName)) throw new DomainException(CommonErrors.SpecialDiscountHolderMissing, "A special discount needs the holder's name.");
        if (string.IsNullOrWhiteSpace(idNumber)) throw new DomainException(CommonErrors.SpecialDiscountHolderMissing, $"A {program} discount needs the {idLabel}.");
        if (rate is <= 0 or >= 1) throw new DomainException(CommonErrors.InvalidDiscountRate, "A discount rate must be a fraction between 0 and 1, such as 0.20.");
        if (vatRateRemoved is < 0 or >= 1) throw new DomainException(CommonErrors.InvalidVatRate, "The VAT rate must be a fraction between 0 and 1, such as 0.12.");
        (Program, IdLabel, HolderName, IdNumber) = (program.Trim(), idLabel.Trim(), holderName.Trim(), idNumber.Trim());
        (Rate, VatRateRemoved) = (rate, vatRateRemoved);
    }

    // The VAT exemption applies only when the seller charges VAT in the first place.
    public static SpecialDiscount Grant(string program, string idLabel, decimal rate, bool vatExempt, string holderName,
        string idNumber, TaxPolicy tax) =>
        new(program, idLabel, holderName, idNumber, rate, vatExempt && tax.Regime == TaxRegime.Vat ? tax.VatRate : 0);

    // VAT first, then the discount on what's left: at 12% and 20%, ₱1,120 → ₱1,000 → ₱800. Worked out on the eligible
    // totals once, not line by line, so the order and the invoice always round the same way.
    public SpecialDiscountAmounts Apply(IEnumerable<(Money Gross, TaxType TaxType, bool Eligible)> lines)
    {
        var eligible = lines.Where(l => l.Eligible).ToList();
        var vatExemptedGross = VatRateRemoved == 0
            ? Money.Zero
            : eligible.Where(l => l.TaxType == TaxType.Vatable).Aggregate(Money.Zero, (sum, l) => sum + l.Gross);
        var vatExemption = vatExemptedGross - new Money(vatExemptedGross.Amount / (1 + VatRateRemoved));
        var discountBase = eligible.Aggregate(Money.Zero, (sum, l) => sum + l.Gross) - vatExemption;
        return new SpecialDiscountAmounts(vatExemptedGross, vatExemption, new Money(discountBase.Amount * Rate));
    }
}

// VatExemptedGross: the VATable sales that became VAT-exempt for this buyer, before the VAT was taken out.
public readonly record struct SpecialDiscountAmounts(Money VatExemptedGross, Money VatExemption, Money Discount)
{
    public static readonly SpecialDiscountAmounts None = new(Money.Zero, Money.Zero, Money.Zero);
    public Money Total => VatExemption + Discount;
}
