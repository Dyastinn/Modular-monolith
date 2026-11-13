# Lesson 22 — Special Discounts: Senior Citizens and Persons with Disability

Good day, developer! In the Philippines, a senior citizen or a person with disability who buys medicine, or
anything else the law covers, for their own use, pays no VAT on it and gets 20% off what's left. Every business
that sells such goods must honor it, and the BIR wants the invoice to show it. Today the order learns to carry
that discount, worked out once, and the invoice prints exactly what the customer was charged. About two hours.
Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `GET /api/special-discount-programs` | Sales readers | The discounts the business grants (`?includeInactive=true` for all) |
| `PUT /api/special-discount-programs/{code}` | SalesManager, Admin | Add or change a program |
| `PUT /api/sales-orders/{id}/special-discount` | Sellers | Apply one to a draft order: program, holder's name, ID number |
| `DELETE /api/sales-orders/{id}/special-discount` | Sellers | Take it off again |

Plus a "qualifies for special discounts" switch per item in the price list, a **Special discounts** tab next
to Categories, a discount dialog on the order, and the discount block on the invoice.

## Why we build it this way

### What the law asks

Two laws give the discount: the Expanded Senior Citizens Act (RA 9994) and the Magna Carta for Persons with
Disability, as amended (RA 10754). On what they cover (medicines, for one), the buyer pays no VAT, and gets 20%
off the price without VAT. The order matters, and the BIR's own example shows it:

| | |
|---|---|
| Price, VAT included | ₱1,120.00 |
| Less the VAT (12%) | ₱120.00 |
| Price without VAT | ₱1,000.00 |
| Less 20% | ₱200.00 |
| **The buyer pays** | **₱800.00** |

And RR 7-2024 Sec. 6(B.18) asks the invoice for three more things: the buyer's ID number, the discount broken
down into the VAT exemption and the discount itself, and a space for the buyer's signature. The same section
covers solo parents, national athletes and coaches, and Medal of Valor awardees, each with their own ID and
rate. A person who is both a senior citizen and a PWD still gets one discount, not two.

### Where the discount is worked out

The obvious place is the invoice: the PDF prints it, so compute it there. Now follow the money. Since lesson 09,
the customer's balance is charged with the **order's** total when it's approved, and lesson 21's payments pay
the **invoice's** total. Discount only the invoice, and the customer is charged ₱1,120 and billed ₱800: their
balance never comes back to zero.

So the order carries the discount, and its total is the discounted one: the credit check, the approval
threshold and the balance all see what the customer will really pay. The invoice must then come to exactly the
same amount. Two calculations, one in each module, would drift the first time somebody changed one of them. So
there's one, in BuildingBlocks, and both modules call it.

To remember it easily: VAT out first, discount second, and the same calculator on both sides of the counter.

### What's a program, and what qualifies

Which programs the business honors, and which of its items qualify, are the business's calls, made with its tax
adviser. A pharmacy marks its medicines; a hardware store may mark nothing at all. So programs are rows in a
table, seeded with the two every business must know, and each item gets a switch in the price list.

Now, let's be honest about what's not built: the separate 5% discount seniors get on basic necessities and
prime commodities, which comes with a weekly purchase cap and a purchase booklet. A business that sells
groceries needs it; [lesson 25](25-production-checklist.md) lists it.

## 1. One calculation, in BuildingBlocks

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/SpecialDiscount.cs
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
```

What to notice:

- **It's a copy, with its VAT rate.** `Grant` reads the seller's `TaxPolicy` once and stores the rate it
  removed. A non-VAT seller removes nothing and gives 20% of the full price. The VAT rate can change next year,
  and an order discounted today still comes to the same total.
- **`Apply` sorts the lines itself.** Only eligible VATable lines lose their VAT; an eligible zero-rated or
  VAT-exempt line has none to lose and is just discounted. Callers hand over every line, so Sales and Invoicing
  can't sort them differently.
- **`VatExemptedGross`** is for the invoice's breakdown: those sales stop being VATable and become VAT-exempt.

It lives in BuildingBlocks, so its two new codes join `CommonErrors`, after lesson 20's tax codes (a bad VAT
rate reuses `ERP-0105`):

```csharp
public static readonly ErrorCode SpecialDiscountHolderMissing = new("ERP-0106", "The discount holder's details are missing",
    "A senior citizen or PWD discount needs the holder's name and ID number, exactly as on the ID card.");
public static readonly ErrorCode InvalidDiscountRate = new("ERP-0107", "Invalid discount rate",
    "Enter the rate as a fraction between 0 and 1: 0.20 for 20%.");
```

## 2. Programs, in Sales

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Domain/SpecialDiscountProgram.cs
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
```

A program is withdrawn, never deleted: old orders still name it. In `SalesDbContext`, `DbSet<SpecialDiscountProgram>
SpecialDiscountPrograms` and its mapping, seeded by the migration itself like lesson 20's company profile:

```csharp
b.Entity<SpecialDiscountProgram>(e =>
{
    e.ToTable("special_discount_program", t => t.HasCheckConstraint("ck_special_discount_program_rate", "rate > 0 AND rate < 1"));
    e.HasKey(x => x.Code);
    e.Property(x => x.Code).HasMaxLength(20);
    e.Property(x => x.Name).HasMaxLength(50);
    e.Property(x => x.IdLabel).HasMaxLength(50);
    e.Property(x => x.Rate).HasPrecision(5, 4);
    // The two every Philippine business must honor (RA 9994, RA 10754): 20% off, and no VAT, on what qualifies.
    e.HasData(
        new { Code = "SC", Name = "Senior citizen", IdLabel = "OSCA ID No.", Rate = 0.20m, VatExempt = true, IsActive = true },
        new { Code = "PWD", Name = "Person with disability", IdLabel = "PWD ID No.", Rate = 0.20m, VatExempt = true, IsActive = true });
});
```

The list is a read, so it's a function, `sales.list_special_discount_programs(p_include_inactive)`, in its own
`.v1.sql` file. Saving is one command, `SaveSpecialDiscountProgramCommand(Code, Name, IdLabel, Rate, VatExempt,
IsActive)`, whose handler finds the program by code and calls `Update`, or adds `Define(...)` when it's new.
Both live in `Features/SpecialDiscounts/` with the order's slices below; the full files are
[SpecialDiscountHandler.cs](../course-validation/business/src/Modules/Sales/Erp.Modules.Sales/Features/SpecialDiscounts/SpecialDiscountHandler.cs)
and its endpoint.

## 3. Items that qualify

`SalesItem` gets one more fact, and lesson 20's `SetPricing` one more parameter. Before:

```csharp
public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale, TaxType taxType)
```

After, with `public bool IsSpecialDiscountEligible { get; private set; }` next to `TaxType`:

```csharp
public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale, TaxType taxType, bool specialDiscountEligible)
```

and `IsSpecialDiscountEligible = specialDiscountEligible;` at its end. The order line copies it, like the price
and the tax type: `SalesOrderLine` gains `SpecialDiscountEligible` and a seventh constructor parameter, and
`SetLines` passes `item.IsSpecialDiscountEligible`. Down the pricing slice, the request gains
`bool SpecialDiscountEligible = false`, and the command and handler pass it on. The unit tests' `Make.Item`
gains `bool discountEligible = false`.

The price list shows it, so `sales.search_price_list.v3.sql` adds `is_special_discount_eligible boolean` at the
end (lesson 15's recipe, third time), and `PriceListItemDto` gains `bool IsSpecialDiscountEligible`.

## 4. The order carries the discount

```csharp
// in SalesOrder
public SpecialDiscount? SpecialDiscount { get; private set; }
// Stored, so the read side can show the total without repeating the calculation in SQL.
public Money SpecialDiscountVatExemption { get; private set; }
public Money SpecialDiscountAmount { get; private set; }

public Money GrossTotal => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
public Money Total => GrossTotal - SpecialDiscountVatExemption - SpecialDiscountAmount;

// One per order: a buyer who is both a senior citizen and a PWD still gets one discount.
public void ApplySpecialDiscount(SpecialDiscount discount)
{
    EnsureDiscountChangeable();
    if (!_lines.Any(l => l.SpecialDiscountEligible))
        throw new DomainException(SalesErrors.NoLineQualifiesForDiscount, $"No item on {OrderNumber} qualifies for the {discount.Program} discount.");
    SpecialDiscount = discount;
    RecalculateSpecialDiscount();
}

public void RemoveSpecialDiscount()
{
    EnsureDiscountChangeable();
    SpecialDiscount = null;
    RecalculateSpecialDiscount();
}

private void RecalculateSpecialDiscount()
{
    var amounts = SpecialDiscount?.Apply(_lines.Select(l => (l.LineTotal, l.TaxType, l.SpecialDiscountEligible)))
        ?? SpecialDiscountAmounts.None;
    (SpecialDiscountVatExemption, SpecialDiscountAmount) = (amounts.VatExemption, amounts.Discount);
}

private void EnsureDiscountChangeable()
{
    if (Status != SalesOrderStatus.Draft)
        throw new DomainException(SalesErrors.DiscountOnlyOnDraft, "A special discount can only be changed on a draft order.");
}
```

Sales' catalog gets the order's three new refusals, plus one for the handler below:

```csharp
public static readonly ErrorCode DiscountProgramEnded = new("SALES-0026", "That discount program is no longer offered",
    "Choose an active program, or ask an admin to reactivate it under Price list → Special discounts.");
public static readonly ErrorCode NoLineQualifiesForDiscount = new("SALES-0027", "No item on the order qualifies for the discount",
    "Only items marked eligible in the price list get a senior citizen or PWD discount.");
public static readonly ErrorCode DiscountOnlyOnDraft = new("SALES-0028", "The discount can only change on a draft order",
    "Cancel the order and create a new one if the discount must change.");
public static readonly ErrorCode DiscountProgramNotFound = new("SALES-0029", "There is no such discount program",
    "Choose a program from the list.");
```

`SetLines` ends with `RecalculateSpecialDiscount();`, so editing a discounted order keeps its discount right.
And notice what *didn't* change: `Submit`, `MarkApproved` and `Cancel` all use `Total`, so the credit check,
the ₱50,000 approval threshold and the customer's balance see the discounted amount without a line of change.
That's the payoff of keeping `Total` in one place since lesson 04.

Why store the two amounts, when `Total` could compute them? Because three SQL functions add up order totals
(the order, the order list and the customer's pending total), and SQL can't call C#. Storing them means SQL
only subtracts. In `SalesDbContext`, the discount is an owned type in the order's own row:

```csharp
e.Ignore(x => x.GrossTotal);
e.OwnsOne(x => x.SpecialDiscount, d =>
{
    d.Property(x => x.Program).HasMaxLength(50);
    d.Property(x => x.IdLabel).HasMaxLength(50);
    d.Property(x => x.HolderName).HasMaxLength(200);
    d.Property(x => x.IdNumber).HasMaxLength(50);
    d.Property(x => x.Rate).HasPrecision(5, 4);
    d.Property(x => x.VatRateRemoved).HasPrecision(5, 4);
});
```

Its columns are named `special_discount_program`, `special_discount_holder_name` and so on, all nullable: no
discount, all `NULL`. Then the read side, lesson 15's recipe once more:

- `sales.get_sales_order.v3.sql`: the total subtracts `o.special_discount_vat_exemption +
  o.special_discount_amount`; new columns at the end bring `gross_total` and the discount's fields; the lines
  function adds `special_discount_eligible`.
- `sales.search_sales_orders.v2.sql` and `sales.search_customers.v3.sql`: the same subtraction in their totals.

```bash
dotnet ef migrations add AddSpecialDiscounts \
  --project src/Modules/Sales/Erp.Modules.Sales --startup-project src/Modules/Sales/Erp.Modules.Sales \
  --output-dir Infrastructure/Migrations
```

Add the four `.sql` files (and the programs function) at the end of `Up`, and the old versions to `Down`.
`SalesOrderDetailDto` gains `decimal GrossTotal` and a `SpecialDiscountDto?`; its line DTO gains
`bool SpecialDiscountEligible`.

## 5. Applying it

```csharp
internal sealed class ApplySpecialDiscountHandler(SalesDbContext db, ICurrentUser currentUser, IInvoicingSetup invoicing)
{
    public async Task<bool> HandleAsync(ApplySpecialDiscountCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        var program = await db.SpecialDiscountPrograms.FindAsync([cmd.ProgramCode.Trim().ToUpperInvariant()], ct)
            ?? throw new DomainException(SalesErrors.DiscountProgramNotFound, $"There is no special discount program '{cmd.ProgramCode}'.");

        order.ApplySpecialDiscount(program.Grant(cmd.HolderName, cmd.IdNumber, await invoicing.GetTaxPolicyAsync(ct)));
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

Load, call the domain, save. The tax policy comes through Invoicing's service window, lesson 20's
`IInvoicingSetup`, which Sales already references since lesson 21. The order in the URL may not exist (`false`,
so 404), but the program code is in the body, so an unknown one is a coded 422, lesson 07's rule. The
request's validator, `ApplySpecialDiscountRequestValidator` in the endpoint file, checks only that the program
code, the name and the ID number are there. `RemoveSpecialDiscountCommand` is the same, shorter.

## 6. The invoice bills what was ordered

Sales' window passes the discount along. In `SalesContracts.cs`:

```csharp
public sealed record OrderForInvoice(
    long OrderId, string OrderNumber, long CustomerId, string CustomerName, string? CustomerTin, string? CustomerAddress,
    int PaymentTermsDays, IReadOnlyList<InvoiceableLine> Lines, SpecialDiscount? SpecialDiscount);
public sealed record InvoiceableLine(long ItemId, string Description, string Unit, decimal Quantity, decimal UnitPrice, TaxType TaxType,
    bool SpecialDiscountEligible);
```

`InvoiceLine` gains `SpecialDiscountEligible` (an optional last constructor parameter), and `Invoice` gains the
discount and its two amounts, set in `Issue`, which takes an optional `SpecialDiscount? specialDiscount = null`
at the end:

```csharp
// The same calculation Sales used for the order, so the invoice bills exactly what the customer was charged.
var discount = specialDiscount?.Apply(lines.Select(l => (l.LineTotal, l.TaxType, l.SpecialDiscountEligible)))
    ?? SpecialDiscountAmounts.None;
invoice.SpecialDiscount = specialDiscount is null ? null : specialDiscount with { };
(invoice.SpecialDiscountVatExemption, invoice.SpecialDiscountAmount) = (discount.VatExemption, discount.Discount);
invoice.Total = lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal) - discount.Total;
var tax = TaxBreakdown.Of(lines, taxPolicy, discount);
```

And the breakdown moves the exempted sales, net of their VAT, from VATable to VAT-exempt:

```csharp
// Prices include VAT, so the VAT is taken out of the VATable total once, not line by line.
var vatableGross = Sum(l => l.TaxType == TaxType.Vatable) - discount.VatExemptedGross;
var net = new Money(vatableGross.Amount / (1 + policy.VatRate));
return new(net, vatableGross - net, Sum(l => l.TaxType == TaxType.ZeroRated),
    exempt + discount.VatExemptedGross - discount.VatExemption);
```

(and for an exempt seller, `Sum(_ => true) - discount.VatExemption`). The issuer passes `l.SpecialDiscountEligible`
and `order.SpecialDiscount`. Check it with this lesson's example: a ₱1,120 medicine and a ₱112 widget give
₱100 VATable, ₱12 VAT, ₱1,000 VAT-exempt, less ₱200 discount: ₱912, the same as the order. Easy, right?

Map the discount on the invoice exactly as on the order, then `invoicing.search_invoices.v3.sql` adds the
discount's columns to `get_invoice` and `special_discount_eligible` to `get_invoice_lines`, in a migration
`AddSpecialDiscounts` for Invoicing too. `InvoiceDetailDto` gains an `InvoiceSpecialDiscountDto?` and a computed
`GrossSales => Total + VatExemption + Discount`, which the percentage-tax breakdown now uses.

The PDF prints Sec. 6(B.18), after the totals:

```csharp
// Sec. 6(B.18): the holder's ID, the discount broken down into the VAT exemption and the discount, and a signature.
if (i.SpecialDiscount is { } d)
{
    var block = section.AddParagraph($"{d.Program} discount (items marked *)");
    block.Format.Font.Bold = true;
    block.Format.SpaceBefore = Unit.FromPoint(12);
    section.AddParagraph($"Name: {d.HolderName}    {d.IdLabel}: {d.IdNumber}");
    var detail = section.AddTable();
    detail.AddColumn(Unit.FromCentimeter(14.4));
    detail.AddColumn(Unit.FromCentimeter(3.0));
    Total(detail, "Sales eligible for the discount", i.Lines.Where(l => l.SpecialDiscountEligible).Sum(l => l.LineTotal));
    if (d.VatExemption > 0) Total(detail, $"Less: {Percent(d.VatRateRemoved)} VAT exemption", d.VatExemption);
    Total(detail, $"Less: {Percent(d.Rate)} discount", d.Discount);
    var signature = section.AddParagraph("Signature: ________________________________");
    signature.Format.SpaceBefore = Unit.FromPoint(18);
}
```

Each eligible line gets a `* ` before its description, and the totals gain "Less: VAT exemption" and "Less: 20%
… discount" rows above the amount due (a small `LessDiscount` helper writes both). Compare with
[InvoicePdf.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Pdf/InvoicePdf.cs).

## 7. The screens

| Screen | What changes |
|---|---|
| **Price list → Set price** | A "Qualifies for special discounts" switch; a **SC/PWD** column in the list |
| **Price list → Special discounts** (new tab) | The programs (code, name, ID asked for, rate, VAT-exempt, offered), with an add/edit form for managers, like lesson 17's Categories |
| **One order** | On a draft with an eligible item, a **Special discount** button. Its dialog lists the offered programs, then asks for the holder's name and the ID number. The totals show gross, the VAT exemption, the discount and the total, with eligible lines starred; a card shows the holder and ID; **Remove discount** takes it off |
| **One invoice** | The same deduction rows, with the holder and ID |

`Format.Percent(0.20m)` gives "20%". The new files are
[SpecialDiscountDialog.razor](../course-validation/business/src/Erp.Web/Pages/Sales/SpecialDiscountDialog.razor) and
[SpecialDiscountPrograms.razor](../course-validation/business/src/Erp.Web/Pages/Sales/SpecialDiscountPrograms.razor);
the API client gains a `Delete(url, success)` next to `Put`.

## 8. Tests

Sales' unit tests get a `SpecialDiscountTests` class:

```csharp
[Fact]
public void A_senior_pays_no_VAT_and_then_20_percent_less_on_eligible_items_only()
{
    var order = Draft(Make.Customer(), (Make.Item(1, 1120, discountEligible: true), 1), (Make.Item(2, 112), 1));

    order.ApplySpecialDiscount(Senior());

    order.SpecialDiscountVatExemption.Should().Be(new Money(120));
    order.SpecialDiscountAmount.Should().Be(new Money(200));
    order.Total.Should().Be(new Money(912));   // ₱1,120 → ₱1,000 → ₱800, plus the ₱112 item at full price
}
```

with `A_non_VAT_seller_gives_the_discount_without_a_VAT_exemption`,
`Changing_the_lines_recalculates_the_discount_and_removing_it_restores_the_total`,
`A_discount_needs_an_ID_an_eligible_item_and_a_draft_order`, and, on the program,
`A_program_rate_is_a_fraction_and_a_withdrawn_program_grants_nothing`. Invoicing gets
`A_senior_discount_moves_the_sale_to_VAT_exempt_and_shows_both_deductions`, which issues the same two lines and
checks the breakdown and the ₱912.

The integration test is the one that matters most, because it crosses every module:
`A_senior_citizen_discount_is_billed_as_ordered_and_withheld_tax_is_tracked_to_its_2307` marks the Gadget
eligible, orders 28 Gadgets (₱1,120) and 4 Widgets (₱100), applies the SC discount, and checks the order:
₱1,220 gross, ₱900 total. Then it ships, waits for the invoice, and checks that it's ₱900 too. It finishes
lesson 21's way: paid with ₱8.89 withheld, a balance of zero, and a 2307 that closes the gap. Copy it from
[BusinessFlowTests.cs](../course-validation/business/tests/Erp.Integration.Tests/BusinessFlowTests.cs).

### The discount, for any order

The examples above use the BIR's own numbers. Two properties check every other order FsCheck can think of, in
`tests/Erp.Modules.Sales.UnitTests/PropertyTests.cs`:

```csharp
public class SpecialDiscountProperties
{
    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);

    private static List<(Money Gross, TaxType TaxType, bool Eligible)> Lines(PositiveInt[] centavos, bool[] eligible) =>
        centavos.Take(8).Select((c, i) => (
            new Money(c.Get % 10_000_000 / 100m),
            (TaxType)(i % 3),
            eligible.Length > 0 && eligible[i % eligible.Length])).ToList();

    private static SpecialDiscount Senior() =>
        SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-1", Vat12);

    [Property]
    public bool The_discount_never_exceeds_what_the_eligible_items_cost(PositiveInt[] centavos, bool[] eligible)
    {
        var lines = Lines(centavos, eligible);
        var amounts = Senior().Apply(lines);
        var eligibleGross = lines.Where(l => l.Eligible).Sum(l => l.Gross.Amount);
        return amounts.Total.Amount <= eligibleGross;
    }

    // The order and the invoice list their lines in their own order; the discount must not care.
    [Property]
    public bool The_order_of_the_lines_never_changes_the_discount(PositiveInt[] centavos, bool[] eligible)
    {
        var lines = Lines(centavos, eligible);
        var reversed = Enumerable.Reverse(lines).ToList();
        return Senior().Apply(lines) == Senior().Apply(reversed);
    }
}
```

The second one is the property the whole design rests on: Sales computes the discount on the order, Invoicing
computes it again at issue, and the two must agree to the centavo. If the result depended on the order of the
lines (round each line, then add them, in whatever order a query returned them), the invoice could differ from
the order the customer was charged for. Computing on the totals, once, makes it impossible, and this proves it
for a hundred random orders.

In the Invoicing unit tests, one more property, next to lesson 20's:

```csharp
// With a senior's discount the breakdown shows the sales before the 20%, so it adds back to total + discount.
[Property]
public bool With_a_special_discount_the_breakdown_adds_back_to_the_total_plus_the_discount(
    PositiveInt[] centavos, PositiveInt[] thousandths, bool[] eligible)
```

(the reference build's `PropertyTests.cs` has it in full: the same `Issue` helper, with eligibility and a
discount passed in).

And two rules for `RulesFoundByMutationTests`:

```csharp
[Fact]
public void A_discount_cannot_be_removed_after_submitting()
{
    var customer = Make.Customer();
    var order = Make.Order(customer, unitPrice: 25);
    order.Submit(customer, Money.Zero, Make.Now);

    order.Invoking(o => o.RemoveSpecialDiscount()).Should().Throw<DomainException>().WithMessage("*draft*");
}

[Theory]
[InlineData("0")]
[InlineData("1")]
public void A_program_rate_of_nothing_or_everything_is_refused(string rate) =>
    FluentActions.Invoking(() => SpecialDiscountProgram.Define("SP", "Solo parent", "ID", decimal.Parse(rate), vatExempt: false))
        .Should().Throw<DomainException>();
```

A rate of 0 is no discount; a rate of 1 gives the goods away. Both are typos the screen must refuse.

| Test | The edge | Why it matters |
|---|---|---|
| `The_discount_never_exceeds_what_the_eligible_items_cost` | Any order | A discount larger than the sale |
| `The_order_of_the_lines_never_changes_the_discount` | Any order of lines | The invoice always equals the order |
| `With_a_special_discount_the_breakdown_adds_back_to_the_total_plus_the_discount` | Any discounted invoice | The B.18 block reconciles |
| `A_discount_needs_an_ID_an_eligible_item_and_a_draft_order` | Nothing; when | No ID, nothing eligible, or too late |
| `A_discount_cannot_be_removed_after_submitting` | When | The customer was charged for the discounted total |
| `A_program_rate_of_nothing_or_everything_is_refused` | 0 and 1 | The two ends that make no sense |
| `A_senior_citizen_discount_is_billed_as_ordered_and_withheld_tax_is_tracked_to_its_2307` | End to end | Order, invoice, payment and 2307 agree |

## How a request flows

```mermaid
sequenceDiagram
  participant R as Rep
  participant S as Sales
  participant V as Invoicing
  R->>S: PUT /api/sales-orders/42/special-discount { "SC", "Lola Basyang", "OSCA-0001" }
  S->>V: IInvoicingSetup.GetTaxPolicyAsync → VAT 12%
  S->>S: program.Grant → SpecialDiscount(20%, VAT 12% removed); order.Apply → ₱120 + ₱200 stored; total ₱900
  R->>S: POST /submit → credit check and balance use ₱900
  Note over S,V: shipped → ShipmentRecorded → issuer
  V->>S: GetForInvoiceAsync(42) → lines with eligibility, the SpecialDiscount
  V->>V: same SpecialDiscount.Apply → ₱120 + ₱200; invoice total ₱900
```

## Mistakes to avoid

- Discounting the invoice only. The customer's balance was charged the order's total; the two must match.
- Taking 20% of the VAT-inclusive price. VAT comes out first: ₱1,120 is ₱800, not ₱896, for a VAT seller.
- Two discounts for one person who is both senior and PWD. One per order.
- Reading today's VAT rate when the invoice is issued. The discount carries the rate it removed.
- Deleting a program. Withdraw it; orders still name it.
- Marking items eligible without the tax adviser. The law decides what qualifies, not the developer.

## Let's run it!

Restart the app; Sales and Invoicing migrate. Then:

1. As the manager, open **Price list → Special discounts**: Senior citizen and Person with disability, 20%,
   VAT-exempt. On **Items**, set the Gadget's price again with "Qualifies for special discounts" on.
2. As the rep, create an order for Acme Corp: 28 Gadgets and 4 Widgets, ₱1,220. Open it, click **Special
   discount**, choose Senior citizen, holder "Lola Basyang", OSCA ID "OSCA-0001". The totals read: gross
   ₱1,220.00, less VAT exemption ₱120.00, less 20% discount ₱200.00, total **₱900.00**.
3. Submit it. As the manager, **Customers**: Acme's balance went up by ₱900, not ₱1,220.
4. As the clerk, ship it. As the accountant, open the invoice: ₱900.00, and **Print** shows the starred
   Gadget line, the VAT-exempt sales of ₱1,000.00, both deductions, and the "Senior citizen discount" block
   with the ID and a signature line.
5. Try to break it: a draft with only Widgets shows no **Special discount** button. Call the API directly with
   `curl -X PUT .../special-discount` anyway, and the answer is a 422: no item qualifies.

## Let's check!

1. Why does the order, not the invoice, compute the discount?
2. Work out a senior's price for a ₱560 VAT-inclusive medicine, step by step.
3. Why does `SpecialDiscount` store the VAT rate it removed?
4. Why are the two discount amounts stored on the order, when `Total` could compute them?
5. What does the BIR ask the invoice to show for a special discount?
6. Who decides which items qualify, and where is that recorded?

And that's where this lesson ends. The ERP now honors the discounts the law gives, on the order and on the
invoice alike, to the centavo. What's left is making it ready for the outside world. Next time: security
hardening. Good luck, developer!

---

← [Lesson 21 — Payments and receivables](21-payments.md) · Next: [Lesson 23 — Security hardening](23-security-hardening.md) →
