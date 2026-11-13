using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A discount the business grants by law (BIR RR 7-2024 Sec. 6(B.18)): senior citizens and PWDs come seeded; solo parents,
// national athletes and others can be added. Which apply, and at what rate, is the business's call with its tax adviser.
internal sealed class SpecialDiscountProgram
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string IdLabel { get; private set; } = default!;
    public decimal Rate { get; private set; }
    public bool VatExempt { get; private set; }
    public bool IsActive { get; private set; }

    private SpecialDiscountProgram() { }

    public static SpecialDiscountProgram Define(string code, string name, string idLabel, decimal rate, bool vatExempt)
    {
        var program = new SpecialDiscountProgram { Code = code.Trim().ToUpperInvariant() };
        program.Update(name, idLabel, rate, vatExempt, isActive: true);
        return program;
    }

    public void Update(string name, string idLabel, decimal rate, bool vatExempt, bool isActive)
    {
        if (rate is <= 0 or >= 1) throw new DomainException(CommonErrors.InvalidDiscountRate, "A discount rate must be a fraction between 0 and 1, such as 0.20.");
        (Name, IdLabel, Rate, VatExempt, IsActive) = (name.Trim(), idLabel.Trim(), rate, vatExempt, isActive);
    }

    // Copied onto the order, with the VAT rate it removes, so later changes here never touch an order already discounted.
    public SpecialDiscount Grant(string holderName, string idNumber, TaxPolicy tax)
    {
        if (!IsActive) throw new DomainException(SalesErrors.DiscountProgramEnded, $"The {Name} discount is no longer offered.");
        return SpecialDiscount.Grant(Name, IdLabel, Rate, VatExempt, holderName, idNumber, tax);
    }
}
