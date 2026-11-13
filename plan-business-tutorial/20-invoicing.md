# Lesson 20 — Invoicing: Every Shipment Billed, Every Number Accounted For

Good day, developer! Goods have left the building, and now the business wants to be paid. Today a fifth
module, Invoicing, bills every shipment by itself, a moment after the clerk clicks Ship. Its invoice numbers
never skip, because an auditor will ask about any gap. An issued invoice is never edited: a wrong one is
voided, with a reason, and issued again. Every invoice prints as a PDF laid out the way the BIR's invoicing
rules ask, with the VAT worked out from the company's own tax setup, kept on a screen, never typed into the
code. About two and a
half hours. Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `GET /api/invoices`, `/api/invoices/{id}` | Accountant, Admin, SalesManager | Search and read invoices |
| `GET /api/invoices/{id}/pdf` | The same | The invoice as a PDF; every copy after the first says REPRINT |
| `POST /api/invoices/{id}/void` | Accountant, Admin | Void a wrong invoice, with a reason |
| `POST /api/invoices/{id}/reissue` | Accountant, Admin | Bill the same shipment again, under a new number |

Plus the `ShipmentRecorded` event that starts it all, a business calendar date for invoices, a VAT treatment
for every item, the company's registered details and tax setup with their own screen, and the Invoices and
invoice screens.

## Why we build it this way

### Billed by an event

When should an invoice exist? When the goods ship. Shipping is Inventory's transaction (lesson 18), and it
has no business creating invoices. So Inventory raises an integration event, `ShipmentRecorded`, through its
outbox, and Invoicing handles it. Neither module knows the other exists: Inventory announces a fact, and
whoever cares reacts. Tomorrow, a "notify the customer" module could handle the same event without a line
of change in Inventory.

Now, let's be honest about the cost: the invoice appears a second or two *after* the shipment, not in the
same transaction. If Invoicing is down, shipping still works, and the invoice waits in the outbox until
Invoicing is back. For billing, that's exactly what you want: the warehouse never stops because accounting
has a problem.

### Numbers without gaps

Lesson 07 numbered orders with a PostgreSQL sequence. A sequence is fast and safe, but it doesn't promise to
be gap-free: a number taken in a transaction that rolls back is gone. For sales orders, a gap is harmless.
For invoices, a tax auditor asks "where is invoice 42?", and "a transaction rolled back" is not an answer
you want to give.

So invoice numbers come from a **counter row** instead: one row in a `document_counter` table, updated with
`UPDATE ... SET next_value = next_value + 1 RETURNING ...` *inside the invoice's own transaction*. If the
invoice rolls back, so does the counter. The cost: the row lock means two invoices are numbered one at a
time. At a few hundred invoices a day, nobody will ever notice.

### Never edit an invoice

An issued invoice is a legal document; the customer may already have filed their copy. So it's never
changed and never deleted. A mistake (a wrong price, the wrong customer details) is **voided**, with a
reason, and a new invoice is issued for the same shipment. The void one stays, numbered and readable,
stamped VOID.

That's also why an invoice copies the customer's name, TIN and address, and each line's description, unit
and price, at the moment it's issued. If the customer moves next year, last year's invoice still shows where
the goods went.

### The date on an invoice

An invoice has a calendar date, not a moment. And it must be the date where the business is. At 7 a.m. in
Manila, it's still yesterday in UTC; an invoice dated by `DateTime.UtcNow` would carry yesterday's date for
the first eight hours of every working day. So the clock learns the business's own date.

### What the BIR asks of an invoice

Since the Ease of Paying Taxes Act, the **invoice** is the document that proves a sale in the Philippines; an
official receipt is now only a supplementary document. Revenue Regulations 7-2024 (amended by RR 11-2024),
Section 6, lists what every invoice must show, and ours is built from that list:

| The BIR asks for | Where it comes from |
|---|---|
| The seller's registered name, and optionally a trade name | The company profile |
| "VAT Reg TIN" or "Non-VAT Reg TIN", then the TIN with its branch code | The company profile: its tax regime and TIN |
| The registered business address | The company profile |
| The word "Invoice", printed prominently | The company profile's invoice title, which must contain it |
| A serial number, printed prominently, and the date | The invoice: its gap-free number and the business's date |
| Space for the buyer's registered name, address and TIN | The invoice: copied from the customer at issue |
| Quantity, unit cost and description of every line | The invoice lines |
| The total, VAT included, with the VAT shown on its own | The invoice |
| For mixed sales: VATable sales, VAT, zero-rated sales and VAT-exempt sales | Each line's VAT treatment, added up at issue |
| "EXEMPT" for a seller subject to neither VAT nor percentage tax; a seller under percentage tax shows the sales subject to it | The tax regime |
| On a system-generated invoice: "REPRINT" on every copy after the first; at the bottom, the BIR permit number, the approved series range and the date issued | A print counter on the invoice; the permit details in the company profile |

The buyer's TIN is required for a sale of ₱1,000 or more to a VAT-registered buyer (Section 3), which is why
lesson 15 gave customers a TIN.

Now, let's be honest about the limit here: following the list isn't the same as being allowed to issue. A
business that issues invoices from a computer system must register that system with the BIR first, and the
permit it gets back goes into the company profile. A tax adviser should see the printed invoice before the first real
one. [Lesson 25](25-production-checklist.md) puts both on the first-day list. The sources: the BIR's
[RMC 77-2024 digest](https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2077-2024%20Digest.pdf), the
[full text of RR 7-2024](https://www.offshore-management.com.ph/blog/wordpress/wp-content/uploads/2024/05/RR-7-2024_Full.pdf)
(a copy; the BIR's website has the official one), and a plain-language
[guide to the requirements](https://mtfcounsel.com/2025/05/08/how-to-comply-with-the-birs-invoicing-requirements/).

### Tax is business data, not a constant and not a setting

The obvious version is `const decimal VatRate = 0.12m`. It works until the law changes the rate, or until a
buyer of this source isn't VAT-registered at all. Then a business decision needs a developer and a release.

The second-obvious version puts it in `appsettings.json`, next to the connection string. Better, but still
wrong. The accountant can't change it: IT edits a file on the server and restarts the app. Nobody can see who
changed it, or when. And the same goes for the company's registered name, TIN, address and BIR permit, which
the business also owns.

So all of these live in a table, the **company profile**, with a screen an accountant uses. The domain checks
each change as it's saved, and every invoice **stores** the regime, the rate, the breakdown and the seller's
details it was issued with. Change the rate next year, and last year's invoices still say exactly what they
said. `appsettings.json` keeps what describes the *installation*: connection strings, certificates, the mail
server, fonts.

Not every item is taxed the same way, either. Export sales are zero-rated; some goods, like fresh
agricultural products, are VAT-exempt. So each item gets a **VAT treatment** in the price list, and the order
line copies it, just like the price.

To remember it easily: the law sets the rate, the accountant enters it, and each invoice remembers it.

## 1. Today, in the business's time zone

`IClock` grows a second member:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/IClock.cs
namespace Erp.Shared.BuildingBlocks;

// Inject instead of DateTime.UtcNow so time-based rules can be tested with a fixed clock.
public interface IClock
{
    DateTime UtcNow { get; }

    // The calendar date where the business is (Company:TimeZone), for invoice and due dates.
    DateOnly Today { get; }
}
```

```csharp
// src/Shared/Erp.Shared.Infrastructure/SystemClock.cs
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure;

public sealed class SystemClock(IConfiguration config) : IClock
{
    // At 7 a.m. in Manila it's still yesterday in UTC; an invoice must carry the local date.
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(config["Company:TimeZone"] ?? "Asia/Manila");

    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone));
}
```

The time zone describes where the server's business is, set once per installation and never by an accountant,
so it's the one company value that belongs in `appsettings.json`:

```json
"Company": { "TimeZone": "Asia/Manila" }
```

Everything else about the company goes into the company profile, in section 6.

`Asia/Manila` works on Windows and Linux alike: .NET 10 understands the standard time zone names on both.

## 2. The shipment announces itself

In `InventoryContracts.cs`, next to `ItemCatalogChanged`:

```csharp
public sealed record ShipmentRecorded(long ShipmentId, string ShipmentNumber, long SalesOrderId, DateTime ShippedUtc)
    : IDomainEvent;
```

And at the end of `Shipment.Record`, before `return shipment;`:

```csharp
shipment.Raise(new ShipmentRecorded(id.Value, number, salesOrderId, nowUtc));
```

This is why lesson 18 took the shipment's id from a sequence before creating it: the event carries the id,
and the outbox writes the event during `SaveChanges`. And it's why `Shipment` was an `AggregateRoot` from
the start: it was always going to announce itself. Lesson 13's rule (raise an event in the change that adds
its handler) holds, because Invoicing's handler arrives in this same lesson.

Inventory's unit tests get their fifth, for the shipment:

```csharp
[Fact]
public void A_shipment_needs_lines_and_is_announced()
{
    FluentActions.Invoking(() => Shipment.Record(new ShipmentId(1), "SH-1", 7, "SO-7", [], Clerk, Now))
        .Should().Throw<DomainException>();

    var shipment = Shipment.Record(new ShipmentId(1), "SH-1", 7, "SO-7", [new ShipmentLine(new ItemId(1), 2)], Clerk, Now);
    shipment.DomainEvents.Should().ContainSingle().Which.Should().Be(new ShipmentRecorded(1, "SH-1", 7, Now));
}
```

## 3. Each item's VAT treatment

The treatment is a fact about a sale that both Sales and Invoicing need, and that crosses Sales' service
window. Contracts reference only BuildingBlocks, so that's where it lives:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/TaxType.cs
namespace Erp.Shared.BuildingBlocks;

// How a sale of an item is treated for VAT (BIR RR 7-2024 Sec. 3(B)). Stored as numbers: never renumber.
public enum TaxType
{
    Vatable = 0,
    ZeroRated = 1,
    VatExempt = 2
}
```

Lesson 04's `SalesItem.SetPricing` learns it. Before:

```csharp
public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale)
```

After, with `public TaxType TaxType { get; private set; }` next to `IsForSale`:

```csharp
public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale, TaxType taxType)
{
    if (!Enum.IsDefined(taxType)) throw new DomainException(SalesErrors.UnknownTaxType, $"'{taxType}' is not a tax type.");
    if (isForSale && unitPrice == Money.Zero) throw new DomainException(SalesErrors.PriceRequiredForSale, $"Set a price for {Sku} before putting it on sale.");
    if (isForSale && !IsActive) throw new DomainException(SalesErrors.InactiveItemNotSellable, $"{Sku} is inactive in the catalog and can't be sold.");
    UnitPrice = unitPrice;
    CategoryId = category.Id;
    Category = category;
    IsForSale = isForSale;
    TaxType = taxType;
}
```

`SalesOrderLine` gets the same property and a sixth constructor parameter, `TaxType taxType`, and
`SalesOrder.AddLine` passes `item.TaxType` along with `item.UnitPrice`. The line copies the treatment for the
same reason it copies the price: the invoice bills what was ordered, not what the price list says today. Lesson
03's unit tests call `SetPricing` in two places; add `TaxType.Vatable` to both.

Down the slice: `SetItemPricingCommand` and `SetItemPricingRequest` gain `TaxType TaxType` at the end (the
request defaults it to `TaxType.Vatable`, so lesson 17's test still passes), the validator adds
`RuleFor(x => x.TaxType).IsInEnum();`, and the handler passes `cmd.TaxType` on.

The browser sends `"taxType": "ZeroRated"`, but System.Text.Json reads an enum only as a number by default,
so that request fails with a 400. Teach the whole API, once, to read and write enums by name. In
`Program.cs`, with the other services:

```csharp
// Enums travel as their names ("ZeroRated"), not numbers, in both directions.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

(add `using System.Text.Json.Serialization;`). Names are also safer than numbers on the wire: a client from an
older version that sends `2` meaning something else would silently record the wrong thing.

The price list must show it, and that changes a released function's result: lesson 15's recipe.
`sales.search_price_list.v2.sql` starts with `DROP FUNCTION IF EXISTS sales.search_price_list(text, boolean,
integer, integer);`, then creates it with `tax_type integer` at the end (selecting `i.tax_type`), and
recreates `count_price_list` as it was. `PriceListItemDto` gains `TaxType TaxType` at the end. Then:

```bash
dotnet ef migrations add AddTaxTypes \
  --project src/Modules/Sales/Erp.Modules.Sales --startup-project src/Modules/Sales/Erp.Modules.Sales \
  --output-dir Infrastructure/Migrations
```

EF adds a `tax_type` column, default 0, to `sales.item` and `sales.sales_order_line`, so every existing item
and order line is VATable, which is what they were. Add the `.v2.sql` at the end of `Up` and put version 1
back in `Down`.

On screen, `PriceListItem` in `Models.cs` gains `string TaxType`. The price list shows it in a **VAT** column
(`@Format.Words(context.TaxType)`), and `PricingDialog` gets a select above the "For sale" switch:

```razor
<MudSelect T="string" @bind-Value="_taxType" Label="VAT treatment" HelperText="Printed on the invoice's VAT breakdown">
    <MudSelectItem Value="@("Vatable")">VATable</MudSelectItem>
    <MudSelectItem Value="@("ZeroRated")">Zero-rated</MudSelectItem>
    <MudSelectItem Value="@("VatExempt")">VAT-exempt</MudSelectItem>
</MudSelect>
```

with `private string _taxType = "Vatable";`, loaded from `Item.TaxType` and sent as `taxType = _taxType`.

## 4. What Invoicing asks of Sales

To bill an order, Invoicing needs its lines with prices and VAT treatments, and the customer's details and
terms. Sales' window gets one more method and two records, in `SalesContracts.cs`:

```csharp
public sealed record OrderForInvoice(
    long OrderId, string OrderNumber, long CustomerId, string CustomerName, string? CustomerTin, string? CustomerAddress,
    int PaymentTermsDays, IReadOnlyList<InvoiceableLine> Lines);
public sealed record InvoiceableLine(long ItemId, string Description, string Unit, decimal Quantity, decimal UnitPrice, TaxType TaxType);

// in ISalesOrders
Task<OrderForInvoice> GetForInvoiceAsync(long orderId, CancellationToken ct);
```

```csharp
// in SalesOrdersService
public async Task<OrderForInvoice> GetForInvoiceAsync(long orderId, CancellationToken ct)
{
    var id = new SalesOrderId(orderId);
    var order = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException($"Sales order {orderId} not found.");
    var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, ct);

    return new OrderForInvoice(order.Id.Value, order.OrderNumber, customer.Id.Value, customer.Name, customer.Tin,
        customer.Address, customer.PaymentTermsDays,
        order.Lines.Select(l => new InvoiceableLine(l.ItemId.Value, l.ItemName, l.Unit, l.Quantity.Value, l.UnitPrice.Amount, l.TaxType)).ToList());
}
```

No transaction this time: the invoice is issued after the shipment committed, and reading a shipped order
can't race anything. The prices come from the order's lines, as ordered, never from today's price list.
Lesson 04's line snapshot pays off here too.

## 5. The module and its domain

Create `Erp.Modules.Invoicing` the way you created Purchasing, referencing BuildingBlocks and the Sales and
Inventory Contracts, and register it in `Program.cs` after Purchasing, as `AddInvoicingModule(builder.Configuration)`
(it reads the font settings, coming up). Add it to `options.Assemblies` and the architecture tests' `Modules`.

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Domain/Invoice.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct InvoiceId(long Value);
internal readonly record struct UserId(long Value);   // owned by Identity

// Stored as numbers: never renumber.
internal enum InvoiceStatus { Issued = 0, Void = 3 }

// How the seller is registered with the BIR. Stored as numbers: never renumber.
internal enum TaxRegime
{
    Vat = 0,             // VAT-registered: prices include VAT, invoices show the VAT breakdown
    PercentageTax = 1,   // non-VAT, subject to percentage tax: invoices show sales subject to percentage tax
    Exempt = 2           // non-VAT, subject to neither: invoices say "EXEMPT"
}

// The seller's tax setup at the moment an invoice is issued. Comes from configuration; copied onto every invoice.
internal readonly record struct TaxPolicy
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

// The totals RR 7-2024 Sec. 6(B.13) requires: VATable sales (net of VAT), the VAT, zero-rated and VAT-exempt sales.
internal readonly record struct TaxBreakdown(Money VatableSales, Money VatAmount, Money ZeroRatedSales, Money VatExemptSales)
{
    public static TaxBreakdown Of(IReadOnlyList<InvoiceLine> lines, TaxPolicy policy)
    {
        Money Sum(Func<InvoiceLine, bool> include) => lines.Where(include).Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        var exempt = Sum(l => l.TaxType == TaxType.VatExempt);

        if (policy.Regime == TaxRegime.Exempt)
            return new(Money.Zero, Money.Zero, Money.Zero, Sum(_ => true));
        if (policy.Regime == TaxRegime.PercentageTax)
            return new(Money.Zero, Money.Zero, Money.Zero, exempt);

        // Prices include VAT, so the VAT is taken out of the VATable total once, not line by line.
        var vatableGross = Sum(l => l.TaxType == TaxType.Vatable);
        var net = new Money(vatableGross.Amount / (1 + policy.VatRate));
        return new(net, vatableGross - net, Sum(l => l.TaxType == TaxType.ZeroRated), exempt);
    }
}

// What was billed, as it was billed: nothing here follows later changes to the customer or the item.
internal sealed class InvoiceLine
{
    public string Description { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public TaxType TaxType { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private InvoiceLine() { }

    public InvoiceLine(string description, string unit, Quantity quantity, Money unitPrice, TaxType taxType)
    { Description = description; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; TaxType = taxType; }
}

// An invoice is never deleted or edited. A mistake is voided, with a reason, and a new one issued.
internal sealed class Invoice : Entity<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public string Number { get; private set; } = default!;
    public SellerDetails Seller { get; private set; } = default!;   // section 6
    public long ShipmentId { get; private set; }     // owned by Inventory
    public long SalesOrderId { get; private set; }   // owned by Sales
    public string OrderNumber { get; private set; } = default!;
    public long CustomerId { get; private set; }     // owned by Sales
    public string CustomerName { get; private set; } = default!;
    public string? CustomerTin { get; private set; }
    public string? CustomerAddress { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public Money Total { get; private set; }
    public TaxRegime TaxRegime { get; private set; }
    public decimal VatRate { get; private set; }
    public Money VatableSales { get; private set; }
    public Money VatAmount { get; private set; }
    public Money ZeroRatedSales { get; private set; }
    public Money VatExemptSales { get; private set; }
    public string? VoidReason { get; private set; }
    public UserId? VoidedBy { get; private set; }
    public DateTime? VoidedUtc { get; private set; }

    private Invoice() { }

    public static Invoice Issue(string number, SellerDetails seller, long shipmentId, long salesOrderId, string orderNumber, long customerId,
        string customerName, string? customerTin, string? customerAddress, int paymentTermsDays,
        IReadOnlyList<InvoiceLine> lines, DateOnly issueDate, TaxPolicy taxPolicy)
    {
        if (lines.Count == 0) throw new DomainException(InvoicingErrors.InvoiceNeedsLines, "An invoice needs at least one line.");
        var invoice = new Invoice
        {
            Number = number, Seller = seller with { }, ShipmentId = shipmentId, SalesOrderId = salesOrderId, OrderNumber = orderNumber,
            CustomerId = customerId, CustomerName = customerName, CustomerTin = customerTin, CustomerAddress = customerAddress,
            IssueDate = issueDate, DueDate = issueDate.AddDays(paymentTermsDays), Status = InvoiceStatus.Issued
        };
        invoice._lines.AddRange(lines);
        invoice.Total = lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        var tax = TaxBreakdown.Of(lines, taxPolicy);
        (invoice.TaxRegime, invoice.VatRate) = (taxPolicy.Regime, taxPolicy.VatRate);
        (invoice.VatableSales, invoice.VatAmount, invoice.ZeroRatedSales, invoice.VatExemptSales) =
            (tax.VatableSales, tax.VatAmount, tax.ZeroRatedSales, tax.VatExemptSales);
        return invoice;
    }

    public void Void(string reason, UserId by, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(InvoicingErrors.VoidReasonRequired, "Voiding an invoice needs a reason.");
        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedBy = by;
        VoidedUtc = nowUtc;
    }

    public void EnsureCanBeReissued()
    {
        if (Status != InvoiceStatus.Void) throw new DomainException(InvoicingErrors.OnlyVoidInvoiceReissued, $"Only a void invoice can be reissued; {Number} is {Status}.");
    }

    private void EnsureNotVoid()
    {
        if (Status == InvoiceStatus.Void) throw new DomainException(InvoicingErrors.InvoiceIsVoid, $"{Number} is void.");
    }
}
```

What to notice:

- **`Total` is stored here**, unlike `SalesOrder.Total`. An invoice never changes after it's issued, so a
  stored total can't drift from its lines, and every report can sum it without touching lines.
- **The due date is the issue date plus the customer's terms**, worked out once, at issue. Changing a
  customer's terms later doesn't move old due dates.
- **The two statuses are numbered 0 and 3.** The numbers in between belong to "partly paid" and "paid",
  arriving with payments next lesson. A stored number is decided once.
- **The VAT split.** A VAT-registered seller's prices include VAT, and the invoice must show how much of the
  total is VAT. At 12%, ₱1,120.00 of VATable sales is ₱1,000.00 net plus ₱120.00 of VAT. The VAT is the gross
  minus the rounded net, so the two always add back to the gross, to the centavo. And it's worked out once,
  over all the VATable lines, not line by line, so a hundred small lines can't round their way a few centavos
  off.
- **`TaxPolicy` refuses nonsense**: a rate of 1.2 (someone meant 12%, not 120%) throws. And a seller that
  isn't VAT-registered has a rate of 0, whatever was entered, so a non-VAT invoice can never show VAT.
- **Everything about tax, and the seller, is stored**: the regime, the rate, all four totals and the seller's
  details. An invoice is never recalculated from today's company profile. `seller with { }` stores a copy, not
  the profile's own object.

`TaxType`, `TaxRegime` and `TaxPolicy` sit in BuildingBlocks' `Domain/Tax.cs`, because Sales needs the policy
too, in [lesson 22](22-special-discounts.md).

## 6. The company profile, and its screen

One row holds everything the BIR asks about the seller, and how it's taxed:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Domain/CompanyProfile.cs
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
        if (string.IsNullOrWhiteSpace(registeredName)) throw new DomainException(InvoicingErrors.CompanyFieldRequired, "The registered name is required.");
        if (string.IsNullOrWhiteSpace(registeredAddress)) throw new DomainException(InvoicingErrors.CompanyFieldRequired, "The registered address is required.");
        if (!TinFormat().IsMatch(tin.Trim()))
            throw new DomainException(InvoicingErrors.InvalidTin, "The TIN must include its branch code, like 123-456-789-00000.");
        if (!invoiceTitle.Contains("INVOICE", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(InvoicingErrors.InvoiceTitleMissingWord, "The invoice title must contain the word \"Invoice\" (RR 7-2024 Sec. 6(A.3)).");
        if (Text(permitDateIssued) is { } date && !PermitDateFormat().IsMatch(date))
            throw new DomainException(InvoicingErrors.InvalidPermitDate, "The permit's date issued is written mm/dd/yyyy, as on the BIR permit.");

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
            throw new DomainException(InvoicingErrors.CompanyProfileIncomplete, "Fill in the company's registered name, TIN and address (Invoicing → Company and tax) before shipping.");
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
```

What to notice:

- **The rules are the BIR's, and they live in the domain.** A TIN without its five-digit branch code (`00000`
  for the head office), a title without "Invoice", a rate of 1.2 (`TaxPolicy` refuses it): each is a
  `DomainException`, so the screen shows the reason and nothing is saved. `[GeneratedRegex]` builds each
  pattern at compile time; that's why the class is `partial`.
- **`Update` records who and when.** The screen can say "last changed on…", and an auditor can ask.
- **`EnsureReadyToInvoice`.** A blank profile is allowed to exist; invoicing from it isn't.

The mapping gives the profile its table, with exactly one row from the very first migration:

```csharp
b.Entity<CompanyProfile>(e =>
{
    e.ToTable("company_profile", t => t.HasCheckConstraint("ck_company_profile_single_row", "id = 1"));
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedNever();
    e.Property(x => x.RegisteredName).HasMaxLength(200);
    // ...max lengths for the other text columns, as in the reference file
    e.Property(x => x.VatRate).HasPrecision(5, 4);
    e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
    e.Ignore(x => x.Tax);
    e.Ignore(x => x.IsComplete);
    // Every database starts with the one row, blank; the business fills it in on its first day.
    e.HasData(new
    {
        Id = CompanyProfile.SingletonId, RegisteredName = "", Tin = "", RegisteredAddress = "",
        TaxRegime = TaxRegime.Vat, VatRate = 0.12m, InvoiceTitle = "SALES INVOICE"
    });
});
```

and the invoice's copy of the seller is an owned type, stored as `seller_name`, `seller_tin` and so on, in the
invoice's own row:

```csharp
e.OwnsOne(x => x.Seller, s => { s.Property(x => x.Name).HasMaxLength(200); /* ...the other lengths */ });
e.Navigation(x => x.Seller).IsRequired();
```

`InvoicingDbContext` gets `DbSet<CompanyProfile> CompanyProfiles` and one helper:

```csharp
public Task<CompanyProfile> CompanyProfileAsync(CancellationToken ct) =>
    CompanyProfiles.SingleAsync(p => p.Id == CompanyProfile.SingletonId, ct);
```

For development, Invoicing gets a `DevSeed.sql` of its own (and its `SeedDevelopmentDataAsync` runs it, as
Sales' does), so invoices can be issued from the first run:

```sql
-- Development only, and safe to run again: fills in the blank company profile with a demo company.
UPDATE invoicing.company_profile
SET registered_name = 'Demo Trading Corp.', trade_name = 'Demo Hardware', tin = '000-111-222-00000',
    registered_address = 'Ortigas Center, Pasig City'
WHERE id = 1 AND registered_name = '';
```

### The screen

Two slices in `Features/CompanyProfile/`, both for `InvoicingRoles.Accountants`. `GET /company-profile` reads
through `invoicing.get_company_profile()`, a one-row SQL function in its own `.v1.sql` file, added to the
`AddReadFunctions` migration. `PUT /company-profile` sends:

```csharp
internal sealed record UpdateCompanyProfileCommand(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress,
    TaxRegime TaxRegime, decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued)
    : ICommand;
```

Its validator checks only shape (required fields, lengths, `IsInEnum`); its handler is load, `Update`, save:

```csharp
var profile = await db.CompanyProfileAsync(ct);
profile.Update(cmd.RegisteredName, cmd.TradeName, cmd.Tin, cmd.RegisteredAddress, new TaxPolicy(cmd.TaxRegime, cmd.VatRate),
    cmd.InvoiceTitle, cmd.PermitNumber, cmd.SeriesRange, cmd.PermitDateIssued, new UserId(user.UserId), clock.UtcNow);
await db.SaveChangesAsync(ct);
```

The page, **Invoicing → Company and tax** (`/company`), is a form in three boxes: Seller, Tax (the regime as a
select; the rate as a percentage, disabled unless VAT-registered) and BIR permit. It warns when the profile is
incomplete. Type it from
[CompanyProfile.razor](../course-validation/business/src/Erp.Web/Pages/Invoicing/CompanyProfile.razor).

### No shipment without a way to bill it

An incomplete profile can't stop an invoice once goods are gone: the issuer would fail, retry ten times in the
outbox, and give up. So the check moves forward, to the moment it can still help: shipping. Inventory may ask
Invoicing through its service window, so Invoicing gets its first contract:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/IInvoicingSetup.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Contracts;

// The company's tax setup, kept by Invoicing on its Company and tax screen, for the modules that need it.
public interface IInvoicingSetup
{
    Task<TaxPolicy> GetTaxPolicyAsync(CancellationToken ct);

    // Throws a DomainException until the company's registered details are filled in.
    Task EnsureReadyToInvoiceAsync(CancellationToken ct);
}
```

Invoicing's service window is a project of its own, like Sales' in lesson 18:

```bash
dotnet new classlib -n Erp.Modules.Invoicing.Contracts -o src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
rm src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/Class1.cs
dotnet sln add src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
dotnet add src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts reference src/Shared/Erp.Shared.BuildingBlocks
dotnet add src/Modules/Invoicing/Erp.Modules.Invoicing reference src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
dotnet add src/Modules/Inventory/Erp.Modules.Inventory reference src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
```

As always, delete the new `<PropertyGroup>`, and add the assembly to the architecture tests'
`ContractsAssemblies`. The implementation is two lines on the profile (`InvoicingSetupService`, registered with
`services.AddScoped<IInvoicingSetup, InvoicingSetupService>();`), and `ShipSalesOrderHandler` calls it before it
opens its transaction:

```csharp
// The BIR expects the invoice with the goods; refuse now rather than ship something that can't be billed.
await invoicing.EnsureReadyToInvoiceAsync(ct);
```

`GetTaxPolicyAsync` waits for [lesson 22](22-special-discounts.md), where Sales needs the rate.

## 7. Invoicing's database

`InvoicingDbContext` follows Purchasing's: the `invoicing` schema, a static `Configure`, the outbox mapping,
converters for `InvoiceId`, `UserId`, `Money` and `Quantity`. Three parts are new.

The counter:

```csharp
internal sealed class DocumentCounter
{
    public string Name { get; private set; } = default!;
    public long NextValue { get; private set; }
}
```

```csharp
b.Entity<DocumentCounter>(e =>
{
    e.ToTable("document_counter");
    e.HasKey(x => x.Name);
    e.Property(x => x.Name).HasMaxLength(30);
    e.HasData(new { Name = "invoice", NextValue = 1L });
});
```

```csharp
// Gap-free: the counter row is updated inside the invoice's transaction, so a rollback gives the number back.
public async Task<string> AllocateInvoiceNumberAsync(DateTime nowUtc, CancellationToken ct)
{
    var value = (await Database.SqlQuery<long>(
            $"UPDATE invoicing.document_counter SET next_value = next_value + 1 WHERE name = 'invoice' RETURNING next_value - 1 AS \"Value\"")
        .ToListAsync(ct)).Single();
    return DocumentNumber.Format("SI", value, nowUtc);
}
```

`HasData` seeds the counter's one row through the migration itself, so every database starts with it,
production included. `UPDATE ... RETURNING` takes the number and locks the row in one statement; the lock
holds until the invoice's transaction ends.

And the invoice's mapping says "one live invoice per shipment":

```csharp
e.HasIndex(x => x.ShipmentId).IsUnique().HasFilter("status <> 3");
```

A **filtered unique index**: unique among the invoices that aren't void. A shipment may have any number of
void invoices, but only ever one live one. That's the database's guarantee behind reissuing.

And the print counter. The BIR wants every copy after the first marked "REPRINT", so the invoice counts its
prints. Nothing in the domain decides anything with it, so it's a **shadow property**: a column EF maps that
has no C# property on the entity.

```csharp
e.Property<int>("PrintCount");
e.Property(x => x.VatRate).HasPrecision(5, 4);
```

```csharp
// One statement, so two people printing at once get 1 and 2, never 1 and 1.
public async Task<int?> RecordPrintAsync(long invoiceId, CancellationToken ct) =>
    (await Database.SqlQuery<int>(
            $"UPDATE invoicing.invoice SET print_count = print_count + 1 WHERE id = {invoiceId} RETURNING print_count AS \"Value\"")
        .ToListAsync(ct)).Cast<int?>().SingleOrDefault();
```

`null` means no such invoice. `HasPrecision(5, 4)` keeps a rate like 0.1200 exact. The rest of the mapping
(lines as an owned table, max lengths, an index on customer and status, `xmin`) is the pattern you know; the
full file is
[InvoicingDbContext.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/InvoicingDbContext.cs).

## 8. Issuing, exactly once

The outbox delivers at least once, and a reissue bills the same shipment again on purpose. Both go through
one class:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/InvoiceIssuer.cs
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Infrastructure;

// Bills one shipment. Safe to call again: if the shipment already has a live invoice, that one is returned.
internal sealed class InvoiceIssuer(InvoicingDbContext db, ISalesOrders orders, IClock clock)
{
    public async Task<long> IssueForShipmentAsync(long shipmentId, long salesOrderId, CancellationToken ct)
    {
        if (await LiveInvoiceForAsync(shipmentId, ct) is { } existing) return existing;

        var order = await orders.GetForInvoiceAsync(salesOrderId, ct);
        var company = await db.CompanyProfileAsync(ct);
        company.EnsureReadyToInvoice();
        var lines = order.Lines
            .Select(l => new InvoiceLine(l.Description, l.Unit, new Quantity(l.Quantity), new Money(l.UnitPrice), l.TaxType))
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Taking the next number locks the counter, so two deliveries of the same event are now in single file.
        var number = await db.AllocateInvoiceNumberAsync(clock.UtcNow, ct);
        if (await LiveInvoiceForAsync(shipmentId, ct) is { } raced) return raced;   // rollback: the number isn't used up

        var invoice = Invoice.Issue(number, company.Seller(), shipmentId, order.OrderId, order.OrderNumber, order.CustomerId,
            order.CustomerName, order.CustomerTin, order.CustomerAddress, order.PaymentTermsDays, lines, clock.Today, company.Tax);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice.Id.Value;
    }

    private async Task<long?> LiveInvoiceForAsync(long shipmentId, CancellationToken ct)
    {
        var id = await db.Invoices.Where(i => i.ShipmentId == shipmentId && i.Status != InvoiceStatus.Void)
            .Select(i => i.Id).FirstOrDefaultAsync(ct);
        return id == default ? null : id.Value;
    }
}
```

Three layers of "exactly once", each catching what the one before it can't:

1. **Already invoiced?** Return that invoice. A repeated event is the common case, and it costs one query.
2. **Take the number, then check again.** Two deliveries of the same event racing each other both pass step
   1. Taking the number locks the counter row, so the second waits there, then sees the first one's invoice,
   and returns it. Its transaction rolls back, and with it the number it took: no gap.
3. **The filtered unique index.** If everything above were wrong, PostgreSQL would still refuse a second
   live invoice for one shipment.

The handler is one line:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/ShipmentRecordedHandler.cs
using Erp.Modules.Inventory.Contracts;
using Mediator;

namespace Erp.Modules.Invoicing.Infrastructure;

// Goods left the building: bill them. Idempotent because the issuer returns the existing invoice on a repeat.
internal sealed class ShipmentRecordedHandler(InvoiceIssuer issuer) : INotificationHandler<ShipmentRecorded>
{
    public async ValueTask Handle(ShipmentRecorded e, CancellationToken ct) =>
        await issuer.IssueForShipmentAsync(e.ShipmentId, e.SalesOrderId, ct);
}
```

Register the issuer in `InvoicingModule` with `services.AddScoped<InvoiceIssuer>();`.

## 9. Void and reissue

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Features/VoidInvoice/VoidInvoiceHandler.cs
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record VoidInvoiceCommand(long InvoiceId, string Reason);

internal sealed class VoidInvoiceHandler(InvoicingDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(VoidInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct);
        if (invoice is null) return false;
        invoice.Void(cmd.Reason, new UserId(user.UserId), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

// After voiding a wrong invoice, bill the same shipment again under a new number.
internal sealed record ReissueInvoiceCommand(long VoidInvoiceId);

internal sealed class ReissueInvoiceHandler(InvoicingDbContext db, InvoiceIssuer issuer)
{
    // null: there is no such invoice.
    public async Task<long?> HandleAsync(ReissueInvoiceCommand cmd, CancellationToken ct)
    {
        var voided = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.VoidInvoiceId), ct);
        if (voided is null) return null;
        voided.EnsureCanBeReissued();
        return await issuer.IssueForShipmentAsync(voided.ShipmentId, voided.SalesOrderId, ct);
    }
}
```

Reissuing reads the order *again*, and takes today's company profile. So the usual fix for a wrong invoice is:
correct the customer in Sales (lesson 15), void the invoice, reissue. The new invoice copies the corrected
details. The endpoints are
`POST /invoices/{id:long}/void` (taking `{ reason }`) and `POST /invoices/{id:long}/reissue` (answering
201 with the new id), both for:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Features/InvoicingRoles.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Features;

internal static class InvoicingRoles
{
    public static readonly string[] Accountants = [Roles.Accountant, Roles.Admin];
    public static readonly string[] Readers = [Roles.Accountant, Roles.Admin, Roles.SalesManager];
}
```

Sales managers read invoices (they answer customers' questions about them) but never change one.

## 10. Reading invoices, and the PDF

`invoicing.search_invoices.v1.sql` holds four functions, the patterns from lesson 08:

| Function | Returns |
|---|---|
| `search_invoices(p_customer_id, p_status, p_search, p_page, p_page_size)` | One page: id, number, customer, order number, issue and due dates, status, total. `p_search` matches the invoice number, the order number or the customer's name |
| `count_invoices(p_customer_id, p_status, p_search)` | The total for that search |
| `get_invoice(p_id)` | One invoice's header: the void reason, who voided it and when, then `tax_regime`, `vat_rate`, the four tax totals, `print_count`, and the seller's copied details (`seller_name` through `seller_permit_date_issued`) |
| `get_invoice_lines(p_id)` | Its lines, with `tax_type` |

Apply it with an `AddReadFunctions` migration, and write the two slices (`Features/SearchInvoices/`,
`Features/GetInvoice/`) from lesson 08's pattern. `InvoiceDetailDto` carries the seller (an `InvoiceSellerDto`) and the stored tax fields as they
are, with the regime as its name (`((TaxRegime)h.TaxRegime).ToString()`), each line's `TaxType` the same way,
and `PrintCount`. The handler calculates no tax: it shows what was issued. Compare with the reference build
when you're done; its versions already include the payment columns that next lesson adds.

Now the PDF. Philippine invoices are printed, filed and attached to emails, so the API makes the PDF
itself.

**PDFsharp and MigraDoc** draw PDFs from C#: PDFsharp is the drawing engine, MigraDoc lays out documents
(paragraphs, tables) on top of it. One package has both:

```bash
dotnet add src/Modules/Invoicing/Erp.Modules.Invoicing package PDFsharp-MigraDoc --version 6.2.4
```

That version number is a decision, not a habit. **PDFsharp 6.2.4 is the last MIT-licensed release.** From
6.5, PDFsharp moved to the AGPL-3.0, a copyleft license that would bind every buyer of this source, and it
checks for a license at runtime. We found out the hard way: on 6.5, the first invoice PDF failed with a
`PdfSharpLicenseException`. So the version is pinned, with a comment in `Directory.Packages.props` saying
never to upgrade it, and the third-party notices say why. QuestPDF, the other popular choice, is
revenue-gated for the same reason, so it's out too. This is lesson 01's license rule earning its keep: check
the license *of the version*, every time.

PDFsharp embeds the font it draws with, so it needs the font file:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Pdf/ErpFontResolver.cs
using PdfSharp.Fonts;

namespace Erp.Modules.Invoicing.Infrastructure;

// PDFsharp embeds the font it draws with, so it needs the font file. One family, regular and bold, found on the
// machine: DejaVu Sans in the container image (fonts-dejavu-core), DejaVu or Liberation Sans on a Linux desktop,
// Arial on Windows and macOS, or paths from configuration.
// The fonts come with the operating system, so no font file ships in this source.
internal sealed class ErpFontResolver(string? regularPath, string? boldPath) : IFontResolver
{
    public const string Family = "ErpSans";

    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans.ttf",
        "/usr/share/fonts/liberation-sans-fonts/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
    ];
    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/liberation-sans-fonts/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
    ];

    private readonly Lazy<byte[]> _regular = new(() => Load(regularPath, RegularCandidates));
    private readonly Lazy<byte[]> _bold = new(() => Load(boldPath, BoldCandidates));

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "ErpSans-Bold" : "ErpSans-Regular");

    public byte[] GetFont(string faceName) => faceName == "ErpSans-Bold" ? _bold.Value : _regular.Value;

    private static byte[] Load(string? configured, string[] candidates)
    {
        var path = new[] { configured }.Concat(candidates).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            ?? throw new InvalidOperationException(
                "No font found for invoice PDFs. Install fonts-dejavu-core, or set Invoicing:FontPath and Invoicing:BoldFontPath.");
        return File.ReadAllBytes(path);
    }
}
```

Fonts are licensed too. Arial ships with Windows and macOS, DejaVu or Liberation Sans with Linux, so the app uses
whichever the machine already has, and no font file ships in this source. The [Docker lesson](24-deploy-podman.md) installs DejaVu in the
image. Settings can point elsewhere:

```json
"Invoicing": { "FontPath": "", "BoldFontPath": "" }
```

The resolver is process-wide in PDFsharp, and may be set only once. In `InvoicingModule`:

```csharp
private static readonly Lock FontLock = new();

// in AddInvoicingModule
// PDFsharp's font resolver is process-wide and may be set only once.
lock (FontLock)
    GlobalFontSettings.FontResolver ??= new ErpFontResolver(config["Invoicing:FontPath"], config["Invoicing:BoldFontPath"]);
```

The lock and the `??=` matter in tests, where several app instances start in one process.

The layout itself, `Infrastructure/Pdf/InvoicePdf.cs`, is `Render(InvoiceDetailDto invoice, bool reprint)`.
Everything about the seller comes from the invoice's own copy, so a reprint matches the original. It builds a
MigraDoc `Document` in the order of the BIR's list:

- "REPRINT" at the top right, when `reprint` is true.
- The seller: registered name, trade name, the TIN statement ("VAT Reg TIN …" or "Non-VAT Reg TIN …", from
  the invoice's regime), the address.
- The title, large. Under it, "EXEMPT" for an exempt seller, "VAT-Exempt Sale" or "Zero-Rated Sale" when the
  whole invoice is one of those, and "VOID" with the reason on a void invoice.
- Two columns: "Sold to" with the buyer's registered name, TIN and address (a blank line when there's none, so
  it can be written in), and the number, large, with the dates and the order.
- The lines, each marked "(zero-rated)" or "(VAT-exempt)" when it isn't VATable.
- The totals by regime: VATable sales, VAT with its stored rate, zero-rated and VAT-exempt sales, and the total
  for a VAT seller; sales subject to percentage tax and exempt sales for a percentage-tax seller; just the
  total for an exempt one.
- In the page footer, the permit number, the date issued and the series range, when they're set.

Amounts print as "PHP 1,120.00": the peso sign isn't in every font, and "PHP" is how Philippine invoices
commonly write it anyway. Type it from
[InvoicePdf.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Pdf/InvoicePdf.cs),
leaving out the "payments received" rows at the end; they arrive next lesson.

Printing changes something now (the count), so it's a command, next to the query in `GetInvoiceHandler.cs`:

```csharp
// Printing is recorded, because every copy after the first must say "REPRINT" (RR 7-2024 Sec. 6(B.21)).
internal sealed record PrintInvoiceCommand(long InvoiceId) : ICommand<(string FileName, byte[] Content)?>;

internal sealed class PrintInvoiceHandler(InvoicingDbContext db, ISender sender)
    : ICommandHandler<PrintInvoiceCommand, (string, byte[])?>
{
    public async ValueTask<(string, byte[])?> Handle(PrintInvoiceCommand cmd, CancellationToken ct)
    {
        if (await db.RecordPrintAsync(cmd.InvoiceId, ct) is not { } printCount) return null;
        if (await sender.Send(new GetInvoiceQuery(cmd.InvoiceId), ct) is not { } invoice) return null;
        return ($"{invoice.Number}.pdf", InvoicePdf.Render(invoice, reprint: printCount > 1));
    }
}
```

It counts first, then reads, through `GetInvoiceQuery`. One query reuses another, so the PDF can never show
something the screen doesn't. The endpoint returns a file instead of JSON:

```csharp
app.MapGet("/invoices/{id:long}/pdf", async (long id, ISender sender, CancellationToken ct) =>
        await sender.Send(new PrintInvoiceCommand(id), ct) is { } pdf
            ? Results.File(pdf.Content, "application/pdf", pdf.FileName)
            : Results.NotFound())
    .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
```

Now, let's be honest about the cost here: a `GET` that changes something. We keep it a `GET` because the
browser opens a PDF from a plain link, with the sign-in cookie. The risk is a browser or a tool fetching the
link on its own and counting a print nobody made. The worst that does is mark a first copy REPRINT, which is
the safe direction to be wrong in.

## 11. The screens

`InvoiceSummary`, `InvoiceLine` and `InvoiceDetail` join `Models.cs`, without the payment fields for now.
`StatusChip` gets `"Issued"` amber and `"Void"` red.

| Page | What it shows |
|---|---|
| **Invoices** (`/invoices`) | Lesson 08's list pattern: search by number, order or customer, a status filter, issue and due dates, total |
| **One invoice** (`/invoices/{id}`) | The header (customer, TIN, address, order, dates), the lines (marked when zero-rated or exempt), the totals with the breakdown for its regime, a **Print** button, and for accountants a **Void** button (using lesson 10's `ReasonDialog`) and, on a void invoice, **Reissue** |

The Print button is just a link, labelled by the count:

```razor
<MudButton Href="@($"api/invoices/{Id}/pdf")" Target="_blank" Variant="Variant.Outlined" StartIcon="@Icons.Material.Filled.PictureAsPdf">
    @(_invoice.PrintCount == 0 ? "Print" : "Reprint")
</MudButton>
```

The browser sends the sign-in cookie with it, like any other request, and opens the PDF in a new tab. Type
both pages, then compare with
[Invoices.razor](../course-validation/business/src/Erp.Web/Pages/Invoicing/Invoices.razor) and
[InvoicePage.razor](../course-validation/business/src/Erp.Web/Pages/Invoicing/InvoicePage.razor), skipping their
payment parts.

The menu gets an Invoicing group for `Roles.Accountant, Roles.Admin, Roles.SalesManager`, with **Company and
tax** inside it for accountants and admins only, and the order page
gets a way across, for shipped orders:

```razor
@if (_order.Status == "Shipped")
{
    <AuthorizeView Roles="@Billing">
        <MudButton Href="@Url.With("invoices", ("search", _order.OrderNumber))" Variant="Variant.Outlined">Invoice</MudButton>
    </AuthorizeView>
}
```

with `private static readonly string Billing = $"{Roles.Accountant},{Roles.Admin},{Roles.SalesManager}";`. The
order doesn't know its invoice's id (that's Invoicing's fact), so the button opens the invoice list searched
by order number. One click more, and no module wall crossed.

The UI tests add `"/invoices"` to `pages`.

## 12. Tests

An Invoicing unit test project, like the others, starting with eight tests:
`An_invoice_totals_its_lines_and_is_due_after_the_terms`, `Only_a_void_invoice_can_be_reissued`, three about
tax, and three about the company profile (`A_blank_company_profile_stops_invoicing_until_it_is_filled_in`,
`The_company_profile_refuses_what_the_BIR_would_refuse`, `An_invoice_keeps_the_seller_it_was_issued_with`). These are the kind of tests accountants love:

```csharp
[Fact]
public void The_VAT_split_adds_back_to_the_total_and_is_kept_on_the_invoice()
{
    var invoice = Invoice();
    invoice.VatableSales.Should().Be(new Money(875.67m));
    invoice.VatAmount.Should().Be(new Money(105.08m));
    (invoice.VatableSales + invoice.VatAmount).Should().Be(invoice.Total);
    invoice.VatRate.Should().Be(0.12m);
}

[Fact]
public void Mixed_sales_are_broken_down_into_vatable_zero_rated_and_exempt()
{
    var invoice = Invoice(Vat12, Line("Widget", 1, 112), Line("Export crate", 1, 50, TaxType.ZeroRated), Line("Book", 1, 30, TaxType.VatExempt));

    invoice.VatableSales.Should().Be(new Money(100));
    invoice.VatAmount.Should().Be(new Money(12));
    invoice.ZeroRatedSales.Should().Be(new Money(50));
    invoice.VatExemptSales.Should().Be(new Money(30));
    invoice.Total.Should().Be(new Money(192));
}
```

The third, `The_rate_comes_from_the_policy_and_a_non_vat_seller_charges_no_vat`, issues at 10% and gets ₱10
of VAT on ₱110, issues as a percentage-tax seller and gets no VAT at all, and checks that a rate of 1.2 is
refused.

The tests build their invoices with a `Vat12` policy, a demo `Seller`, a `Line(...)` helper and an `Invoice(...)` helper whose
default is 2 Widgets at ₱25 and 1.5 kg of wire at ₱620.50, issued on November 5 with 30-day terms. Copy the
helpers and the tests from
[InvoiceTests.cs](../course-validation/business/tests/Erp.Modules.Invoicing.UnitTests/InvoiceTests.cs),
leaving out the payment ones; the reissue test ends with a payment line, which you'll add next lesson.

And in `BusinessFlowTests.cs`, the gap check, which reads the numbers straight from the database:

```csharp
[Fact]
public async Task Invoice_numbers_have_no_gaps()
{
    await using var conn = factory.OpenDatabase();
    var numbers = (await conn.QueryAsync<string>("SELECT number FROM invoicing.invoice ORDER BY id")).ToList();
    var counters = numbers.Select(n => int.Parse(n[^6..])).ToList();

    counters.Should().Equal(Enumerable.Range(1, counters.Count));
}
```

The first half of `Shipping_issues_an_invoice_and_a_payment_lowers_the_customer_balance` arrives now too:
ship an order through the API, wait (`Eventually`) for its invoice to appear, check the total, and download
the PDF twice; then `print_count` must be 2, because the second copy was a reprint. Its second half comes with
payments. And `The_company_profile_is_kept_on_a_screen_and_checked_like_the_BIR_would` sends the profile a TIN
without its branch code and a rate of 1.2 (both 422), tries it as a rep (403), then saves it properly (204).

### The breakdown, for any invoice

The tests above check the breakdown for the invoices we thought of. The BIR checks it for every invoice, so
let FsCheck invent a hundred, with every mix of VATable, zero-rated and exempt lines, and odd quantities:

```bash
dotnet add tests/Erp.Modules.Invoicing.UnitTests package FsCheck.Xunit --version 3.6.0
```

```csharp
// tests/Erp.Modules.Invoicing.UnitTests/PropertyTests.cs
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FsCheck;
using FsCheck.Xunit;

namespace Erp.Modules.Invoicing.UnitTests;

// The BIR checks that the printed breakdown adds back to the amount due. FsCheck tries a hundred invoices for us.
public class TaxBreakdownProperties
{
    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);
    private static readonly SellerDetails Seller = new("Demo Trading Corp.", null, "000-111-222-00000", "Pasig", "SALES INVOICE", null, null, null);

    private static Invoice Issue(PositiveInt[] centavos, PositiveInt[] thousandths)
    {
        var count = Math.Min(Math.Min(centavos.Length, thousandths.Length), 8);
        var lines = Enumerable.Range(0, count).Select(i => new InvoiceLine($"Item {i}", "kg",
            new Quantity(thousandths[i].Get % 100_000 / 1000m + 0.001m),
            new Money(centavos[i].Get % 10_000_000 / 100m + 0.01m),
            (TaxType)(i % 3))).ToList();

        return Invoice.Issue("SI-1", Seller, shipmentId: 1, salesOrderId: 1, "SO-1", customerId: 7, "Acme", null, "Makati",
            paymentTermsDays: 30, lines, new DateOnly(2026, 11, 5), Vat12);
    }

    [Property]
    public bool The_breakdown_always_adds_back_to_the_total(PositiveInt[] centavos, PositiveInt[] thousandths)
    {
        if (centavos.Length == 0 || thousandths.Length == 0) return true;
        var invoice = Issue(centavos, thousandths);
        return invoice.VatableSales + invoice.VatAmount + invoice.ZeroRatedSales + invoice.VatExemptSales == invoice.Total;
    }
}
```

`(TaxType)(i % 3)` gives the lines VATable, zero-rated and exempt in turn, so every invoice mixes them. The
VAT is taken out of the VATable total *once*, not line by line, precisely so this property holds: round each
line's VAT and the centavos drift. Try it: compute the net per line in `TaxBreakdown.Of`, and watch FsCheck
find an invoice that's off by one centavo.

`ShipmentRecorded` now writes an invoice, so the redelivery test's snapshot gains two lines:

```csharp
|| '|' || (SELECT COUNT(*) FROM invoicing.invoice)::text
|| '|' || (SELECT COALESCE(SUM(total), 0) FROM invoicing.invoice)::text
```

Redelivering a shipment must never issue a second invoice. `InvoiceIssuer` checks for the shipment's invoice
first, and this proves it.

And the browser flow takes its next step. The invoice is issued by the outbox a moment after the shipment,
so the accountant's list may not show it yet. Keep the order number when the rep submits (right after the
`Pending approval` chip):

```csharp
var orderNumber = await rep.Locator("h4").First.InnerTextAsync();
```

and after the clerk ships:

```csharp
// The invoice appears by itself; the accountant opens it.
var accountant = await SignIn("accountant@erp.local");
var invoiceRow = accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = orderNumber });
for (var attempt = 0; attempt < 15 && await invoiceRow.CountAsync() == 0; attempt++)
{
    await accountant.GotoAsync($"/invoices?search={Uri.EscapeDataString(orderNumber)}");
    await accountant.WaitForTimeoutAsync(1000);
}
await Shot(accountant, "accountant-invoices");
await invoiceRow.First.ClickAsync();
```

The loop is the browser's version of polling the outbox: reload the list, up to fifteen times, until the
invoice is there.

| Test | The edge | Why it matters |
|---|---|---|
| `The_breakdown_always_adds_back_to_the_total` | A hundred invoices nobody wrote by hand | The printed totals always reconcile |
| `Invoice_numbers_have_no_gaps` | At the same instant | A gap in the series is the first thing an auditor asks about |
| `Only_a_void_invoice_can_be_reissued` | When | Two live invoices for one shipment |
| `A_blank_company_profile_stops_invoicing_until_it_is_filled_in` | Nothing | An invoice without the seller's TIN is not an invoice |
| `The_company_profile_refuses_what_the_BIR_would_refuse` | Odd input | A TIN without its branch code, a rate of 12 instead of 0.12 |
| `An_invoice_keeps_the_seller_it_was_issued_with` | After it changed | A reprint must match the original |
| The snapshot lines in the redelivery test | Twice | One shipment, one invoice |

## How a request flows

```mermaid
sequenceDiagram
  participant C as Clerk
  participant I as Inventory
  participant D as Outbox dispatcher
  participant V as Invoicing (InvoiceIssuer)
  participant S as Sales (GetForInvoiceAsync)
  C->>I: POST /api/shipments (order 42)
  I->>I: ship; Shipment.Record raises ShipmentRecorded; COMMIT with outbox row
  D->>V: ShipmentRecorded(shipment 9, order 42)
  V->>V: live invoice for shipment 9? no
  V->>S: GetForInvoiceAsync(42) → lines, customer, terms
  V->>V: BEGIN; UPDATE document_counter ... RETURNING → 17; check again; INSERT invoice SI-2026-000017; COMMIT
```

## Mistakes to avoid

- Numbering invoices with a sequence. A rollback leaves a gap, and an auditor asks about it.
- Editing an issued invoice. Void it, with a reason, and reissue.
- Reading the customer's details when printing. Copy them at issue; the invoice is a snapshot.
- Dating invoices with `DateTime.UtcNow`. Use the business's calendar date.
- Upgrading PDFsharp past 6.2.4. The license changes to AGPL, and the app throws at runtime.
- Typing `0.12` into the code, or into `appsettings.json`. The rate is business data, kept on a screen, and
  each invoice stores the one it used.
- Printing an old invoice with today's company details. Print the invoice's own copy.
- Issuing real invoices before the system is registered with the BIR and the permit is in the company profile.
- Creating the invoice inside the shipping transaction. Inventory would have to know about Invoicing.

## Let's run it!

Restart the app; Invoicing migrates, and its counter starts at 1. Then:

1. As the rep, create and submit an order for Acme: 3 Widgets and 1.5 kg of Copper Wire. As the clerk, ship it.
2. As `accountant@erp.local`, open **Invoicing → Invoices**. Within a couple of seconds, SI-2026-000001 is
   there: Acme Corp, ₱1,005.75, due in 30 days.
3. Open it. The breakdown shows ₱897.99 VATable sales, ₱107.76 VAT (12%), and nothing zero-rated or exempt.
   Click **Print**: the invoice opens with "Demo Trading Corp.", "Demo Hardware" and "VAT Reg TIN
   000-111-222-00000" at the top. Back on the page, the button now says **Reprint**; click it, and that copy
   says REPRINT at the top.
4. Click **Void**, reason "Wrong address". The invoice is stamped Void. Click **Reissue**: SI-2026-000002
   appears for the same shipment.
5. Back on the order (as the manager), the **Invoice** button opens the list searched by its number: two
   invoices, one void, one live.
6. As the manager, open the **Price list**, set the Gadget to VAT-exempt, then order, submit and ship one
   Widget and one Gadget. The new invoice breaks the total into VATable and VAT-exempt sales, and the Gadget's
   line says "(VAT-exempt)".
7. As the accountant, open **Invoicing → Company and tax**. Change the TIN to `000-111-222` and save: refused,
   it needs the branch code. Put it back, switch the regime to "Non-VAT, subject to percentage tax", and save.
   Open the first invoice again: it still shows its 12% VAT and "VAT Reg TIN", because it stored them. Only
   invoices issued from now on say "Non-VAT Reg TIN". Switch it back to VAT-registered.

Now try to break it: in `psql`, look at the outbox and confirm the `ShipmentRecorded` message was processed,
then set its `processed_utc` back to `NULL`. A couple of seconds later the dispatcher delivers it again, and
nothing changes: still one live invoice, and the counter didn't move.

```bash
podman exec -i erp-postgres psql -U postgres -d erp \
  -c "UPDATE inventory.outbox_message SET processed_utc = NULL WHERE type LIKE '%ShipmentRecorded%';" \
  -c "SELECT number, status FROM invoicing.invoice ORDER BY id;"
```

## Let's check!

1. Why is the invoice created by an event, instead of inside the shipping transaction?
2. Why can't invoice numbers come from a sequence, and what does the counter row cost?
3. The same `ShipmentRecorded` event is delivered twice at the same moment. Walk through what stops a second
   invoice, and why no number is lost.
4. Why does an invoice copy the customer's name and address instead of reading them when it's printed?
5. Why is PDFsharp pinned to 6.2.4?
6. At 7 a.m. in Manila, what date does a new invoice carry, and why?
7. The VAT rate changes next year. Who changes it, where, and what happens to this year's invoices?
8. Why does an order line copy the item's VAT treatment, and why does `TaxType` live in BuildingBlocks?

And that's where this lesson ends. Every shipment is billed by itself, the numbers stand up to an audit, and
the customer gets a PDF that shows what the BIR asks for. Next time, the money comes in: payments, and the report that tells the
accountant who's late. Good luck, developer!

---

← [Lesson 19 — Purchasing](19-purchasing.md) · Next: [Lesson 21 — Payments and receivables](21-payments.md) →
