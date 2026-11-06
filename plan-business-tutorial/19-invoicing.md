# Lesson 19 — Invoicing: Every Shipment Billed, Every Number Accounted For

Good day, developer! Goods have left the building, and now the business wants to be paid. Today a fifth
module, Invoicing, bills every shipment by itself, a moment after the clerk clicks Ship. Its invoice numbers
never skip, because an auditor will ask about any gap. An issued invoice is never edited: a wrong one is
voided, with a reason, and issued again. And every invoice prints as a PDF the customer can file. About two
hours. Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `GET /api/invoices`, `/api/invoices/{id}` | Accountant, Admin, SalesManager | Search and read invoices |
| `GET /api/invoices/{id}/pdf` | The same | The invoice as a PDF |
| `POST /api/invoices/{id}/void` | Accountant, Admin | Void a wrong invoice, with a reason |
| `POST /api/invoices/{id}/reissue` | Accountant, Admin | Bill the same shipment again, under a new number |

Plus the `ShipmentRecorded` event that starts it all, a business calendar date for invoices, and the Invoices
and invoice screens.

## Why we build it this way

### Billed by an event

When should an invoice exist? When the goods ship. Shipping is Inventory's transaction (lesson 17), and it
has no business creating invoices. So Inventory raises an integration event, `ShipmentRecorded`, through its
outbox, and Invoicing handles it. Neither module knows the other exists: Inventory announces a fact, and
whoever cares reacts. Tomorrow, a "notify the customer" module could handle the same event without a line
of change in Inventory.

Now, let's be honest about the cost: the invoice appears a second or two *after* the shipment, not in the
same transaction. If Invoicing is down, shipping still works, and the invoice waits in the outbox until
Invoicing is back. For billing, that's exactly what you want: the warehouse never stops because accounting
has a problem.

### Numbers without gaps

Lesson 06 numbered orders with a PostgreSQL sequence. A sequence is fast and safe, but it doesn't promise to
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

The time zone is a setting, because the buyer of this source may not be in Manila. The company's own details
go next to it in `appsettings.json`, printed on every invoice:

```json
"Company": { "Name": "", "Tin": "", "Address": "", "VatRegistered": true, "TimeZone": "Asia/Manila" }
```

Empty in `appsettings.json`, filled in by each installation; [lesson 24](24-production-checklist.md) lists it
as a first-day step. For development, `appsettings.Development.json` gets demo values:

```json
"Company": { "Name": "Demo Trading Corp.", "Tin": "000-111-222-000", "Address": "Ortigas Center, Pasig City" }
```

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

This is why lesson 17 took the shipment's id from a sequence before creating it: the event carries the id,
and the outbox writes the event during `SaveChanges`. And it's why `Shipment` was an `AggregateRoot` from
the start: it was always going to announce itself. Lesson 11's rule (raise an event in the change that adds
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

## 3. What Invoicing asks of Sales

To bill an order, Invoicing needs its lines with prices, and the customer's details and terms. Sales'
window gets one more method and two records, in `SalesContracts.cs`:

```csharp
public sealed record OrderForInvoice(
    long OrderId, string OrderNumber, long CustomerId, string CustomerName, string? CustomerTin, string? CustomerAddress,
    int PaymentTermsDays, IReadOnlyList<InvoiceableLine> Lines);
public sealed record InvoiceableLine(long ItemId, string Description, string Unit, decimal Quantity, decimal UnitPrice);

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
        order.Lines.Select(l => new InvoiceableLine(l.ItemId.Value, l.ItemName, l.Unit, l.Quantity.Value, l.UnitPrice.Amount)).ToList());
}
```

No transaction this time: the invoice is issued after the shipment committed, and reading a shipped order
can't race anything. The prices come from the order's lines, as ordered, never from today's price list.
Lesson 03's line snapshot pays off here too.

## 4. The module and its domain

Create `Erp.Modules.Invoicing` the way you created Purchasing, referencing BuildingBlocks and the Sales and
Inventory Contracts, and register it in `Program.cs` after Purchasing, as `AddInvoicingModule(builder.Configuration)`
(it reads font settings, coming up). Add it to `options.Assemblies` and the architecture tests' `Modules`.

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Domain/Invoice.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct InvoiceId(long Value);
internal readonly record struct UserId(long Value);   // owned by Identity

// Stored as numbers: never renumber.
internal enum InvoiceStatus { Issued = 0, Void = 3 }

// What was billed, as it was billed: nothing here follows later changes to the customer or the item.
internal sealed class InvoiceLine
{
    public string Description { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private InvoiceLine() { }

    public InvoiceLine(string description, string unit, Quantity quantity, Money unitPrice)
    { Description = description; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; }
}

// An invoice is never deleted or edited. A mistake is voided, with a reason, and a new one issued.
internal sealed class Invoice : Entity<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public string Number { get; private set; } = default!;
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
    public string? VoidReason { get; private set; }
    public UserId? VoidedBy { get; private set; }
    public DateTime? VoidedUtc { get; private set; }

    private Invoice() { }

    public static Invoice Issue(string number, long shipmentId, long salesOrderId, string orderNumber, long customerId,
        string customerName, string? customerTin, string? customerAddress, int paymentTermsDays,
        IReadOnlyList<InvoiceLine> lines, DateOnly issueDate)
    {
        if (lines.Count == 0) throw new DomainException("An invoice needs at least one line.");
        var invoice = new Invoice
        {
            Number = number, ShipmentId = shipmentId, SalesOrderId = salesOrderId, OrderNumber = orderNumber,
            CustomerId = customerId, CustomerName = customerName, CustomerTin = customerTin, CustomerAddress = customerAddress,
            IssueDate = issueDate, DueDate = issueDate.AddDays(paymentTermsDays), Status = InvoiceStatus.Issued
        };
        invoice._lines.AddRange(lines);
        invoice.Total = lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        return invoice;
    }

    public void Void(string reason, UserId by, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Voiding an invoice needs a reason.");
        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedBy = by;
        VoidedUtc = nowUtc;
    }

    public void EnsureCanBeReissued()
    {
        if (Status != InvoiceStatus.Void) throw new DomainException($"Only a void invoice can be reissued; {Number} is {Status}.");
    }

    private void EnsureNotVoid()
    {
        if (Status == InvoiceStatus.Void) throw new DomainException($"{Number} is void.");
    }
}

// The VAT split printed on the invoice. Prices are VAT-inclusive; a non-VAT seller shows no split.
internal static class VatBreakdown
{
    public const decimal Rate = 0.12m;

    public static (Money Vatable, Money Vat) Of(Money total) =>
        (new Money(total.Amount / (1 + Rate)), new Money(total.Amount - Math.Round(total.Amount / (1 + Rate), 2, MidpointRounding.AwayFromZero)));
}
```

What to notice:

- **`Total` is stored here**, unlike `SalesOrder.Total`. An invoice never changes after it's issued, so a
  stored total can't drift from its lines, and every report can sum it without touching lines.
- **The due date is the issue date plus the customer's terms**, worked out once, at issue. Changing a
  customer's terms later doesn't move old due dates.
- **The two statuses are numbered 0 and 3.** The numbers in between belong to "partly paid" and "paid",
  arriving with payments next lesson. A stored number is decided once.
- **The VAT split.** In the Philippines, a VAT-registered seller's prices include 12% VAT, and the invoice
  must show how much of the total is VAT. ₱1,120.00 is ₱1,000.00 of VATable sales plus ₱120.00 of VAT. The
  VAT is worked out as the total minus the rounded VATable amount, so the two always add back to the total,
  to the centavo.

## 5. Invoicing's database

`InvoicingDbContext` follows Purchasing's: the `invoicing` schema, a static `Configure`, the outbox mapping,
converters for `InvoiceId`, `UserId`, `Money` and `Quantity`. Two parts are new.

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
void invoices, but only ever one live one. That's the database's guarantee behind reissuing. The rest of the
mapping (lines as an owned table, max lengths, an index on customer and status, `xmin`) is the pattern you
know; the full file is
[InvoicingDbContext.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/InvoicingDbContext.cs).

## 6. Issuing, exactly once

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
        var lines = order.Lines
            .Select(l => new InvoiceLine(l.Description, l.Unit, new Quantity(l.Quantity), new Money(l.UnitPrice)))
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Taking the next number locks the counter, so two deliveries of the same event are now in single file.
        var number = await db.AllocateInvoiceNumberAsync(clock.UtcNow, ct);
        if (await LiveInvoiceForAsync(shipmentId, ct) is { } raced) return raced;   // rollback: the number isn't used up

        var invoice = Invoice.Issue(number, shipmentId, order.OrderId, order.OrderNumber, order.CustomerId, order.CustomerName,
            order.CustomerTin, order.CustomerAddress, order.PaymentTermsDays, lines, clock.Today);
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

## 7. Void and reissue

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Features/VoidInvoice/VoidInvoiceHandler.cs
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record VoidInvoiceCommand(long InvoiceId, string Reason) : ICommand;

internal sealed class VoidInvoiceValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VoidInvoiceHandler(InvoicingDbContext db, ICurrentUser user, IClock clock) : ICommandHandler<VoidInvoiceCommand>
{
    public async ValueTask<Unit> Handle(VoidInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");
        invoice.Void(cmd.Reason, new UserId(user.UserId), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// After voiding a wrong invoice, bill the same shipment again under a new number.
internal sealed record ReissueInvoiceCommand(long VoidInvoiceId) : ICommand<long>;

internal sealed class ReissueInvoiceHandler(InvoicingDbContext db, InvoiceIssuer issuer) : ICommandHandler<ReissueInvoiceCommand, long>
{
    public async ValueTask<long> Handle(ReissueInvoiceCommand cmd, CancellationToken ct)
    {
        var voided = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.VoidInvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.VoidInvoiceId} not found.");
        voided.EnsureCanBeReissued();
        return await issuer.IssueForShipmentAsync(voided.ShipmentId, voided.SalesOrderId, ct);
    }
}
```

Reissuing reads the order *again*. So the usual fix for a wrong invoice is: correct the customer in Sales
(lesson 14), void the invoice, reissue. The new invoice copies the corrected details. The endpoints are
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

## 8. Reading invoices, and the PDF

`invoicing.search_invoices.v1.sql` holds four functions, the patterns from lesson 07:

| Function | Returns |
|---|---|
| `search_invoices(p_customer_id, p_status, p_search, p_page, p_page_size)` | One page: id, number, customer, order number, issue and due dates, status, total. `p_search` matches the invoice number, the order number or the customer's name |
| `count_invoices(p_customer_id, p_status, p_search)` | The total for that search |
| `get_invoice(p_id)` | One invoice's header, including the void reason, who voided it and when |
| `get_invoice_lines(p_id)` | Its lines |

Apply it with an `AddReadFunctions` migration, and write the two slices (`Features/SearchInvoices/`,
`Features/GetInvoice/`) from lesson 07's pattern. `GetInvoiceHandler` adds the VAT split to its DTO when the
company is VAT-registered (`Company:VatRegistered`), using `VatBreakdown.Of`. Compare with the reference
build when you're done; its versions already include the payment columns that next lesson adds.

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
// machine: DejaVu Sans in the Docker image (fonts-dejavu-core), Arial on Windows, or paths from configuration.
// The fonts come with the operating system, so no font file ships in this source.
internal sealed class ErpFontResolver(string? regularPath, string? boldPath) : IFontResolver
{
    public const string Family = "ErpSans";

    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
    ];
    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
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

Fonts are licensed too. Arial ships with Windows and DejaVu with Linux, so the app uses whichever the machine
already has, and no font file ships in this source. The [Docker lesson](22-docker.md) installs DejaVu in the
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

The layout itself, `Infrastructure/Pdf/InvoicePdf.cs`, builds a MigraDoc `Document`: the company's name,
address and TIN at the top; "SALES INVOICE" (or "SALES INVOICE (VOID)"); a two-column block with the
customer on the left and the number, dates and order on the right; a table of lines; and the totals, with
the VAT split when the company is VAT-registered. Amounts print as "PHP 1,120.00": the peso sign isn't in
every font, and "PHP" is how Philippine invoices commonly write it anyway. Type it from
[InvoicePdf.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Pdf/InvoicePdf.cs),
leaving out the "payments received" rows at the end; they arrive next lesson.

The endpoint returns a file instead of JSON:

```csharp
app.MapGet("/invoices/{id:long}/pdf", async (long id, ISender sender, CancellationToken ct) =>
        await sender.Send(new GetInvoicePdfQuery(id), ct) is { } pdf
            ? Results.File(pdf.Content, "application/pdf", pdf.FileName)
            : Results.NotFound())
    .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Readers));
```

`GetInvoicePdfQuery`'s handler sends `GetInvoiceQuery` itself, through `ISender`, then hands the result and
the company's details to `InvoicePdf.Render`. One query reuses another, so the PDF can never show something
the screen doesn't.

## 9. The screens

`InvoiceSummary`, `InvoiceLine` and `InvoiceDetail` join `Models.cs`, without the payment fields for now.
`StatusChip` gets `"Issued"` amber and `"Void"` red.

| Page | What it shows |
|---|---|
| **Invoices** (`/invoices`) | Lesson 07's list pattern: search by number, order or customer, a status filter, issue and due dates, total |
| **One invoice** (`/invoices/{id}`) | The header (customer, TIN, address, order, dates), the lines, the totals with the VAT split, a **PDF** button, and for accountants a **Void** button (using lesson 09's `ReasonDialog`) and, on a void invoice, **Reissue** |

The PDF button is just a link: `<MudButton Href="@($"api/invoices/{Id}/pdf")" Target="_blank">PDF</MudButton>`.
The browser sends the sign-in cookie with it, like any other request, and opens the PDF in a new tab. Type
both pages, then compare with
[Invoices.razor](../course-validation/business/src/Erp.Web/Pages/Invoicing/Invoices.razor) and
[InvoicePage.razor](../course-validation/business/src/Erp.Web/Pages/Invoicing/InvoicePage.razor), skipping their
payment parts.

The menu gets an Invoicing group for `Roles.Accountant, Roles.Admin, Roles.SalesManager`, and the order page
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

## 10. Tests

An Invoicing unit test project, like the others, starting with three tests:
`An_invoice_totals_its_lines_and_is_due_after_the_terms`, `Only_a_void_invoice_can_be_reissued` and
`The_VAT_split_adds_back_to_the_total`. The last one is the kind of test accountants love:

```csharp
[Fact]
public void The_VAT_split_adds_back_to_the_total()
{
    var (vatable, vat) = VatBreakdown.Of(new Money(980.75m));
    vatable.Should().Be(new Money(875.67m));
    vat.Should().Be(new Money(105.08m));
    (vatable + vat).Should().Be(new Money(980.75m));
}
```

The tests build their invoice with `Invoice.Issue(...)`: 2 Widgets at ₱25 and 1.5 kg of wire at ₱620.50,
issued on November 5 with 30-day terms. Copy the helper and the first two tests from
[InvoiceTests.cs](../course-validation/business/tests/Erp.Modules.Invoicing.UnitTests/InvoiceTests.cs); its
reissue test ends with a payment line, which you'll add next lesson.

And in `BusinessFlowTests.cs`, the gap check, which reads the numbers straight from the database:

```csharp
[Fact]
public async Task Invoice_numbers_have_no_gaps()
{
    await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
    var numbers = (await conn.QueryAsync<string>("SELECT number FROM invoicing.invoice ORDER BY id")).ToList();
    var counters = numbers.Select(n => int.Parse(n[^6..])).ToList();

    counters.Should().Equal(Enumerable.Range(1, counters.Count));
}
```

The first half of `Shipping_issues_an_invoice_and_a_payment_lowers_the_customer_balance` arrives now too:
ship an order through the API, wait (`Eventually`) for its invoice to appear, check the total, and download
the PDF. Its second half comes with payments.

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
- Creating the invoice inside the shipping transaction. Inventory would have to know about Invoicing.

## Let's run it!

Restart the app; Invoicing migrates, and its counter starts at 1. Then:

1. As the rep, create and submit an order for Acme: 3 Widgets and 1.5 kg of Copper Wire. As the clerk, ship it.
2. As `accountant@erp.local`, open **Invoicing → Invoices**. Within a couple of seconds, SI-2026-000001 is
   there: Acme Corp, ₱1,005.75, due in 30 days.
3. Open it. The VAT split shows ₱897.99 VATable and ₱107.76 VAT. Click **PDF**: a printable invoice opens,
   with your demo company's name and TIN at the top.
4. Click **Void**, reason "Wrong address". The invoice is stamped Void. Click **Reissue**: SI-2026-000002
   appears for the same shipment.
5. Back on the order (as the manager), the **Invoice** button opens the list searched by its number: two
   invoices, one void, one live.

Now try to break it: in `psql`, look at the outbox and confirm the `ShipmentRecorded` message was processed,
then set its `processed_utc` back to `NULL`. A couple of seconds later the dispatcher delivers it again, and
nothing changes: still one live invoice, and the counter didn't move.

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
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

And that's where this lesson ends. Every shipment is billed by itself, the numbers stand up to an audit, and
the customer gets a proper PDF. Next time, the money comes in: payments, and the report that tells the
accountant who's late. Good luck, developer!

---

← [Lesson 18 — Purchasing](18-purchasing.md) · Next: [Lesson 20 — Payments and receivables](20-payments.md) →
