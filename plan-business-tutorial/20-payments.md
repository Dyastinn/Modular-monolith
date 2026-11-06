# Lesson 20 — Payments and Receivables: The Money Comes In

Good day, developer! Invoices go out; today the money comes back. The accountant records each payment
against its invoice (cash, check, bank transfer, part or whole) and the invoice knows what's still owed. Then
something quietly important happens: the customer's balance in Sales drops, so their next order has room
again, even though Sales never saw the payment. And at the end of the month, one report answers the
accountant's real question: who owes us money, and how late is it? About two hours. Are you ready? Let's
begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `POST /api/invoices/{id}/payments` | Accountant, Admin | Record a payment |
| `POST /api/invoices/{id}/payments/{paymentId}/void` | Accountant, Admin | Undo a payment recorded by mistake, or a bounced check |
| `GET /api/invoices?overdueOnly=true` | Invoicing readers | Invoices past due and not fully paid |
| `GET /api/reports/receivables-aging?asOf=` | Invoicing readers | What each customer owes, by how late |

Plus the `PaymentRecorded` and `PaymentVoided` events, an **applied payments** table that makes Sales apply
each payment exactly once, and the payment dialog and aging screens.

## Why we build it this way

### Two balances, one truth

Since lesson 03, Sales has kept each customer's `OutstandingBalance`: approved orders not yet paid for. It's
what the credit check reads, and it must go down when money comes in. But payments belong to Invoicing.
Sales may not read Invoicing's tables, and Invoicing may not change Sales' customers.

So Invoicing announces each payment with an integration event, and Sales lowers the balance itself, like
lesson 16's catalog copy. A voided payment is announced too, and Sales puts the amount back.

### Exactly once, from at least once

Lesson 11's outbox delivers at least once. For the catalog, delivering twice was harmless: writing the same
name twice changes nothing. For money, it's a disaster: the same ₱50,000 payment applied twice takes ₱100,000
off what the customer owes.

The fix is a small table in Sales, `applied_payment`, with the payment's id as its primary key. Applying a
payment writes its row *and* lowers the balance, in one `SaveChanges`. A second delivery finds the row and
does nothing. If two deliveries race, the second one's insert breaks the primary key, its transaction rolls
back, and the outbox retries it later, when it finds the row. To remember it easily: it's a receipt book. You
never record the same receipt number twice.

This pattern has a name, the **inbox**: the receiving side remembers what it has already handled.

### What if they overpay?

A customer owes ₱300 but pays ₱500. The invoice can't take more than its balance (the domain refuses), so
that happens across invoices: the balance in Sales is lower than what a payment covers, because another
order was cancelled meanwhile. Sales' balance never goes below zero (lesson 03's `DeductFromBalance`), so the
applied payment records what was **actually** deducted, ₱300. If that payment is later voided, Sales puts
back exactly ₱300, not ₱500. The overpayment itself is the accountant's to sort out with the customer, outside
the system.

### Aging

"Receivables aging" is the report every accountant knows: each customer's open amount, split into buckets by
how long past due it is.

| Customer | Not yet due | 1–30 days | 31–60 | 61–90 | Over 90 | Total |
|---|---|---|---|---|---|---|
| Acme Corp | ₱12,000 | ₱3,500 | | | | ₱15,500 |

It's a pure read, answered by one SQL function. And it takes an **as-of date**, so the accountant can run
last month's report today: invoices issued after that day, and payments made after it, don't count.

## 1. Invoices take payments

The invoice grows. Its status gets the two numbers lesson 19 left free:

```csharp
internal enum InvoiceStatus { Issued = 0, PartiallyPaid = 1, Paid = 2, Void = 3 }
internal enum PaymentMethod { Cash = 1, Check = 2, BankTransfer = 3, Other = 4 }

internal readonly record struct PaymentId(long Value);
```

A payment is a child of its invoice, never deleted, only voided:

```csharp
internal sealed class Payment
{
    public PaymentId Id { get; private set; }
    public Money Amount { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }   // check number, bank reference
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public DateTime? VoidedUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoid => VoidedUtc is not null;

    private Payment() { }

    public Payment(PaymentId id, Money amount, DateOnly paidOn, PaymentMethod method, string? reference, UserId by, DateTime nowUtc)
    { Id = id; Amount = amount; PaidOn = paidOn; Method = method; Reference = reference; RecordedBy = by; RecordedUtc = nowUtc; }

    public void Void(string reason, DateTime nowUtc) { VoidedUtc = nowUtc; VoidReason = reason; }
}
```

`Invoice` becomes an `AggregateRoot` (it will raise events) and gains its payments, `AmountPaid` and
`BalanceDue`:

```csharp
internal sealed class Invoice : AggregateRoot<InvoiceId>
{
    private readonly List<Payment> _payments = new();
    public IReadOnlyList<Payment> Payments => _payments;
    public Money AmountPaid { get; private set; }
    public Money BalanceDue => Total - AmountPaid;
    // ... everything from lesson 19; Issue sets AmountPaid = Money.Zero ...

    public void RecordPayment(PaymentId id, Money amount, DateOnly paidOn, PaymentMethod method, string? reference,
        UserId by, DateOnly today, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (amount == Money.Zero) throw new DomainException("A payment must be more than zero.");
        if (amount > BalanceDue)
            throw new DomainException($"{Number} has {BalanceDue} left to pay; {amount} is more than that.");
        if (paidOn < IssueDate) throw new DomainException("A payment can't be dated before the invoice.");
        if (paidOn > today) throw new DomainException("A payment can't be dated in the future.");

        _payments.Add(new Payment(id, amount, paidOn, method, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), by, nowUtc));
        AmountPaid += amount;
        RefreshStatus();
        Raise(new PaymentRecorded(id.Value, CustomerId, amount.Amount));
    }

    // A payment recorded by mistake, or a check that bounced.
    public void VoidPayment(PaymentId id, string reason, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Voiding a payment needs a reason.");
        var payment = _payments.SingleOrDefault(p => p.Id == id) ?? throw new DomainException($"Payment {id.Value} is not on {Number}.");
        if (payment.IsVoid) throw new DomainException("That payment is already void.");

        payment.Void(reason.Trim(), nowUtc);
        AmountPaid -= payment.Amount;
        RefreshStatus();
        Raise(new PaymentVoided(id.Value, CustomerId, payment.Amount.Amount));
    }

    private void RefreshStatus() =>
        Status = BalanceDue == Money.Zero ? InvoiceStatus.Paid
            : AmountPaid > Money.Zero ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Issued;
}
```

And voiding the invoice itself learns one more rule, in `Void`, after the reason check:

```csharp
if (AmountPaid > Money.Zero) throw new DomainException($"{Number} has payments. Void them first.");
```

What each rule protects:

- **No payment over the balance.** Money in can never make an invoice "more than paid".
- **No payment dated before the invoice, or in the future.** Both are typing mistakes that would wreck the
  aging report. "Future" means after *today in the business's time zone*, lesson 19's `clock.Today`, passed
  in as `today` so the rule stays testable.
- **The status follows the money.** `RefreshStatus` works it out from the amounts every time; it's never set
  by hand. Void a payment on a paid invoice, and it's partly paid again.
- **An invoice with live payments can't be voided.** First the money goes back (void the payments, which
  tells Sales), then the invoice. The order of undoing is the reverse of the order of doing.

## 2. The events, in Invoicing's window

Invoicing gets a Contracts project, because Sales must handle its events:

```bash
dotnet new classlib -n Erp.Modules.Invoicing.Contracts -o src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
rm src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/Class1.cs
dotnet sln add src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
dotnet add src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts reference src/Shared/Erp.Shared.BuildingBlocks
dotnet add src/Modules/Invoicing/Erp.Modules.Invoicing reference src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
dotnet add src/Modules/Sales/Erp.Modules.Sales reference src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts
```

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/InvoicingContracts.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Contracts;

// Sales lowers the customer's balance when money comes in, and raises it again if the payment is voided.
// Delivered at least once: handlers must apply each PaymentId once.
public sealed record PaymentRecorded(long PaymentId, long CustomerId, decimal Amount) : IDomainEvent;
public sealed record PaymentVoided(long PaymentId, long CustomerId, decimal Amount) : IDomainEvent;
```

And lesson 16's trap, avoided this time on purpose: the new Contracts assembly goes into `options.Assemblies`
in `Program.cs`, and into the architecture tests' `ContractsAssemblies`.

```csharp
options.Assemblies =
[
    typeof(IdentityModule), typeof(SalesModule), typeof(InventoryModule), typeof(PurchasingModule), typeof(InvoicingModule),
    typeof(Erp.Modules.Inventory.Contracts.ItemCatalogChanged), typeof(Erp.Modules.Invoicing.Contracts.PaymentRecorded)
];
```

Notice the arrows. Sales now references Invoicing's Contracts, and Invoicing references Sales' Contracts.
Both point at Contracts, so there's still no cycle.

## 3. Invoicing stores payments

In `InvoicingDbContext`: payments are owned by the invoice, like lines, but with a real key (each has an id
from a sequence, because the event carries it):

```csharp
public async Task<PaymentId> NextPaymentIdAsync(CancellationToken ct) =>
    new((await Database.SqlQuery<long>($"SELECT nextval('invoicing.payment_id_seq') AS \"Value\"").ToListAsync(ct)).Single());
```

```csharp
b.HasSequence<long>("payment_id_seq");

// inside the Invoice configuration
e.Ignore(x => x.BalanceDue);
e.Ignore(x => x.DomainEvents);
e.OwnsMany(x => x.Payments, p =>
{
    p.ToTable("payment", t => t.HasCheckConstraint("ck_payment_amount", "amount > 0"));
    p.WithOwner().HasForeignKey("InvoiceId");
    p.HasKey(x => x.Id);
    p.Property(x => x.Id).ValueGeneratedNever();
    p.Property(x => x.Reference).HasMaxLength(100);
    p.Property(x => x.VoidReason).HasMaxLength(500);
    p.Ignore(x => x.IsVoid);
});
e.Navigation(x => x.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
```

and the invoice table gets a check, `amount_paid >= 0 AND amount_paid <= total`. Recording a payment
changes the invoice's own row too (`AmountPaid`, `Status`), so the `xmin` check on the invoice already
catches two accountants recording against the same invoice at once.

```bash
dotnet ef migrations add AddPayments \
  --project src/Modules/Invoicing/Erp.Modules.Invoicing --startup-project src/Modules/Invoicing/Erp.Modules.Invoicing \
  --output-dir Infrastructure/Migrations
```

## 4. Recording and voiding payments

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Features/RecordPayment/RecordPaymentHandler.cs
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record RecordPaymentCommand(long InvoiceId, decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference)
    : ICommand<long>;

internal sealed class RecordPaymentValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

// Saving the payment also writes PaymentRecorded to the outbox; Sales lowers the customer's balance from it.
internal sealed class RecordPaymentHandler(InvoicingDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<RecordPaymentCommand, long>
{
    public async ValueTask<long> Handle(RecordPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");

        var paymentId = await db.NextPaymentIdAsync(ct);
        invoice.RecordPayment(paymentId, new Money(cmd.Amount), cmd.PaidOn, cmd.Method, cmd.Reference, new UserId(user.UserId),
            clock.Today, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return paymentId.Value;
    }
}

internal sealed record VoidPaymentCommand(long InvoiceId, long PaymentId, string Reason) : ICommand;

internal sealed class VoidPaymentValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VoidPaymentHandler(InvoicingDbContext db, IClock clock) : ICommandHandler<VoidPaymentCommand>
{
    public async ValueTask<Unit> Handle(VoidPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct)
            ?? throw new NotFoundException($"Invoice {cmd.InvoiceId} not found.");
        invoice.VoidPayment(new PaymentId(cmd.PaymentId), cmd.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

One `SaveChangesAsync` writes the payment, the invoice's new totals, and the event in the outbox. There's no
"tell Sales" call anywhere in the handler: the domain raised the event, and lesson 11's interceptor did the
rest.

The endpoints, in `RecordPaymentEndpoint.cs`, take a `RecordPaymentRequest(decimal Amount, DateOnly PaidOn,
PaymentMethod Method, string? Reference)` and a `VoidRequest(string Reason)`, both for
`InvoicingRoles.Accountants`. (Move `VoidRequest` here from lesson 19's void endpoint: both voids share it.)

The method arrives as `"BankTransfer"` from the app, but System.Text.Json reads an enum only as a number by
default, so the request fails with a 400. Teach the whole API, once, to read and write enums by name. In
`Program.cs`, with the other services:

```csharp
// Enums travel as their names ("Cash"), not numbers, in both directions.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

(add `using System.Text.Json.Serialization;`). Names are also safer than numbers on the wire: a client that
sends `3` meaning "Check" from an older version would silently record a bank transfer.

## 5. Sales applies each payment once

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Domain/AppliedPayment.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A payment Invoicing told us about, applied to the customer's balance exactly once. The row is what makes the
// at-least-once event handler idempotent: a second delivery finds it and does nothing.
internal sealed class AppliedPayment
{
    public long PaymentId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public Money Amount { get; private set; }   // what was actually deducted: less than paid if they overpaid
    public DateTime AppliedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }

    private AppliedPayment() { }

    public static AppliedPayment Apply(long paymentId, Customer customer, Money paid, DateTime nowUtc)
    {
        var deducted = Money.Min(paid, customer.OutstandingBalance);
        customer.DeductFromBalance(deducted);
        return new AppliedPayment { PaymentId = paymentId, CustomerId = customer.Id, Amount = deducted, AppliedUtc = nowUtc };
    }

    public void Reverse(Customer customer, DateTime nowUtc)
    {
        if (ReversedUtc is not null) return;   // already reversed: a repeated event
        customer.AddToBalance(Amount);
        ReversedUtc = nowUtc;
    }
}
```

It's a domain class, because "deduct only what's owed, and give back only what was deducted" is a business
rule. Its key is Invoicing's payment id, a plain `long`: Sales may not use Invoicing's `PaymentId` type.

In `SalesDbContext`: `public DbSet<AppliedPayment> AppliedPayments => Set<AppliedPayment>();` and

```csharp
b.Entity<AppliedPayment>(e =>
{
    e.ToTable("applied_payment");
    e.HasKey(x => x.PaymentId);
    e.Property(x => x.PaymentId).ValueGeneratedNever();   // Invoicing's id
});
```

then a Sales migration, `AddAppliedPayments`.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Infrastructure/EventHandlers/PaymentHandlers.cs
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

// Money in lowers what the customer owes, so their next order has room again. The applied_payment row makes a
// second delivery of the same event a no-op.
internal sealed class PaymentRecordedHandler(SalesDbContext db, IClock clock) : INotificationHandler<PaymentRecorded>
{
    public async ValueTask Handle(PaymentRecorded e, CancellationToken ct)
    {
        if (await db.AppliedPayments.AnyAsync(p => p.PaymentId == e.PaymentId, ct)) return;

        var customer = await db.Customers.SingleAsync(c => c.Id == new CustomerId(e.CustomerId), ct);
        db.AppliedPayments.Add(AppliedPayment.Apply(e.PaymentId, customer, new Money(e.Amount), clock.UtcNow));
        await db.SaveChangesAsync(ct);   // a racing duplicate fails on the primary key, and the outbox retries it
    }
}

internal sealed class PaymentVoidedHandler(SalesDbContext db, IClock clock) : INotificationHandler<PaymentVoided>
{
    public async ValueTask Handle(PaymentVoided e, CancellationToken ct)
    {
        var applied = await db.AppliedPayments.SingleOrDefaultAsync(p => p.PaymentId == e.PaymentId, ct);
        if (applied is null) return;   // never applied, so nothing to undo

        var customer = await db.Customers.SingleAsync(c => c.Id == applied.CustomerId, ct);
        applied.Reverse(customer, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
```

The customer's `xmin` from lesson 09 protects this too: a payment lowering the balance while a manager
approves an order that raises it can't lose either change; the loser gets a concurrency error, and the outbox
retries it with fresh numbers.

Is the `if (... AnyAsync ...) return;` a business `if` in a handler? It's the inbox check, a technical
guarantee about deliveries, not a rule of the business. The rules (what to deduct, what to give back) are in
`AppliedPayment`.

One more Sales unit test, in `CustomerRuleTests`, proves all three rules of `AppliedPayment`:

```csharp
[Fact]
public void A_payment_never_takes_the_balance_below_zero()
{
    var customer = Make.Customer(balance: 300);
    var applied = AppliedPayment.Apply(paymentId: 9, customer, new Money(500), Make.Now);

    customer.OutstandingBalance.Should().Be(Money.Zero);
    applied.Amount.Should().Be(new Money(300));   // only what was owed

    applied.Reverse(customer, Make.Now);
    customer.OutstandingBalance.Should().Be(new Money(300));
    applied.Reverse(customer, Make.Now);   // a repeated event changes nothing
    customer.OutstandingBalance.Should().Be(new Money(300));
}
```

## 6. Reads: version 2 of the invoice search, and the aging report

Invoices now need `amount_paid` in their results, an "overdue" filter, and their payments. That changes
lesson 19's released functions, so it's lesson 14's recipe: `invoicing.search_invoices.v2.sql` starts by
dropping `search_invoices`, `count_invoices` and `get_invoice` (their parameters or results change), then
creates:

- `search_invoices(p_customer_id, p_status, p_search, p_overdue_on date, p_page, p_page_size)`, returning
  `amount_paid` after `total`. When `p_overdue_on` is set, only invoices with `due_date < p_overdue_on`, not
  void and not fully paid.
- `count_invoices` with the same four filters.
- `get_invoice(p_id)`, with `amount_paid` after `total`.
- `get_invoice_lines(p_id)`, unchanged.
- `get_invoice_payments(p_id)`: each payment's id, amount, date, method (as a number), reference, who
  recorded it and when, and its void details.

The file is exactly the reference build's
[search_invoices file](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Sql/Functions/invoicing.search_invoices.v1.sql)
with the `DROP FUNCTION` lines added at the top (the reference build ships each function at its final shape,
the same way it squashes migrations). The handlers pass `OverdueOn = q.OverdueOnly ? clock.Today : null`,
and `GetInvoiceHandler` reads the payments as a third result set, turning `method` back into its name.
`GET /invoices` gains a `bool? overdueOnly` parameter.

The aging report is one function:

```sql
-- invoicing.receivables_aging.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- What each customer owed on p_as_of, by how late it was. Payments made after that day don't count yet.
CREATE OR REPLACE FUNCTION invoicing.receivables_aging(p_as_of date)
RETURNS TABLE (customer_id bigint, customer_name text, not_yet_due numeric, days_1_to_30 numeric, days_31_to_60 numeric,
               days_61_to_90 numeric, over_90 numeric, total numeric)
LANGUAGE sql STABLE
AS $$
    WITH open_invoice AS (
        SELECT i.customer_id, i.customer_name, i.due_date,
               i.total - COALESCE((SELECT SUM(p.amount) FROM invoicing.payment p
                                   WHERE p.invoice_id = i.id AND p.voided_utc IS NULL AND p.paid_on <= p_as_of), 0) AS open_amount
        FROM invoicing.invoice i
        WHERE i.status <> 3 AND i.issue_date <= p_as_of
    )
    SELECT o.customer_id, MAX(o.customer_name)::text,
           SUM(CASE WHEN p_as_of <= o.due_date THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 1 AND 30 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 31 AND 60 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 61 AND 90 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date > 90 THEN o.open_amount ELSE 0 END),
           SUM(o.open_amount)
    FROM open_invoice o
    WHERE o.open_amount > 0
    GROUP BY o.customer_id
    ORDER BY MAX(o.customer_name);
$$;
```

Read the `WITH` part first: a **common table expression**, a named step inside the query. It works out each
invoice's open amount *as of that day*: its total, minus the live payments made on or before it. Then the
main query sorts those amounts into buckets with `CASE`. In PostgreSQL, subtracting one `date` from another
gives a number of days, which is what makes `p_as_of - o.due_date BETWEEN 1 AND 30` read so plainly.

It reads only Invoicing's own tables: the customer's name was copied onto each invoice at issue. That's
lesson 19's snapshot paying off a second time.

Ship both files in one migration, `AddPaymentReads`, with the old functions put back in `Down`. The slice:

```csharp
// src/Modules/Invoicing/Erp.Modules.Invoicing/Features/ReceivablesAging/ReceivablesAgingHandler.cs
using Dapper;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Invoicing.Features;

internal sealed record ReceivablesAgingQuery(DateOnly? AsOf) : IQuery<ReceivablesAgingDto>;

internal sealed record AgingRowDto(long CustomerId, string CustomerName, decimal NotYetDue, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

internal sealed record ReceivablesAgingDto(DateOnly AsOf, IReadOnlyList<AgingRowDto> Customers, AgingRowDto Totals);

internal sealed class ReceivablesAgingHandler(IDbConnectionFactory connections, IClock clock)
    : IQueryHandler<ReceivablesAgingQuery, ReceivablesAgingDto>
{
    public async ValueTask<ReceivablesAgingDto> Handle(ReceivablesAgingQuery q, CancellationToken ct)
    {
        var asOf = q.AsOf ?? clock.Today;
        await using var conn = connections.Create();
        var rows = (await conn.QueryAsync<AgingRowDto>(new CommandDefinition(
            "SELECT * FROM invoicing.receivables_aging(@AsOf);", new { AsOf = asOf }, cancellationToken: ct))).ToList();

        var totals = new AgingRowDto(0, "Total", rows.Sum(r => r.NotYetDue), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90), rows.Sum(r => r.Over90), rows.Sum(r => r.Total));
        return new ReceivablesAgingDto(asOf, rows, totals);
    }
}
```

`GET /reports/receivables-aging` takes an optional `DateOnly? asOf` (today, in the business's time zone, if
it's missing), for `InvoicingRoles.Readers`. Dapper fills the `DateOnly` parameter through lesson 18's type
handler. The totals row is summed in C# from the rows already read; a second query would add nothing.

The PDF gets its payment rows back. In `InvoicePdf`, the model gains `decimal AmountPaid`, and after the
"Total amount due" row:

```csharp
if (m.AmountPaid > 0)
{
    Total(totals, "Less payments received", m.AmountPaid);
    Total(totals, "Balance due", m.Total - m.AmountPaid, bold: true);
}
```

## 7. The screens

`Models.cs`: `InvoiceSummary` gains `AmountPaid` and `BalanceDue`; `InvoiceDetail` gains `AmountPaid`,
`BalanceDue` and `List<Payment> Payments`; and three new shapes:

```csharp
public sealed record Payment(long Id, decimal Amount, DateOnly PaidOn, string Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

public sealed record AgingRow(long CustomerId, string CustomerName, decimal NotYetDue, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

public sealed record AgingReport(DateOnly AsOf, List<AgingRow> Customers, AgingRow Totals);
```

`StatusChip` learns `"Paid"` (green) and `"PartiallyPaid"` (amber).

| Screen | What changes |
|---|---|
| **Invoices** | A **Balance due** column, and an "Overdue only" switch that adds `overdueOnly=true` (and reads it from the address, for the tile below) |
| **One invoice** | Paid and balance rows under the total; a **Payments** table with each payment's date, method, reference, amount and who recorded it (lesson 15's user directory), void ones struck through with their reason; a **Record payment** button while there's a balance; a **Void** link on each live payment (`ReasonDialog` again) |
| **Payment dialog** | Amount (starting at the balance due), date (`MudDatePicker`, starting today), method (`MudSelect` over the four names), reference |
| **Receivables aging** (`/receivables-aging`) | An as-of date picker and a `MudSimpleTable` of the report, with the totals row in bold |

Type them, then compare with the reference build's
[Invoicing pages](../course-validation/business/src/Erp.Web/Pages/Invoicing/).

The menu's Invoicing group gets **Receivables aging**, and the accountant's home page gets two tiles:

```razor
<AuthorizeView Roles="@AccountantRoles">
    <MudItem xs="12" sm="6" md="4">
        <Tile Title="Overdue invoices" Text="Past their due date and not fully paid." Href="invoices?overdueOnly=true" Icon="@Icons.Material.Filled.Alarm" />
    </MudItem>
    <MudItem xs="12" sm="6" md="4">
        <Tile Title="Receivables aging" Text="Who owes what, and how late." Href="receivables-aging" Icon="@Icons.Material.Filled.Assessment" />
    </MudItem>
</AuthorizeView>
```

with `private static readonly string AccountantRoles = $"{Roles.Accountant},{Roles.Admin}";`.

## 8. Tests

The Invoicing unit tests reach seven: add `Payments_move_it_to_partly_paid_then_paid_and_tell_Sales`,
`A_payment_can_never_exceed_the_balance_or_be_dated_oddly`, `Voiding_a_payment_reopens_the_balance_and_tells_Sales`
and `An_invoice_with_payments_cannot_be_voided` from
[InvoiceTests.cs](../course-validation/business/tests/Erp.Modules.Invoicing.UnitTests/InvoiceTests.cs), and the
last line of `Only_a_void_invoice_can_be_reissued`. The first shows the events Sales will receive:

```csharp
[Fact]
public void Payments_move_it_to_partly_paid_then_paid_and_tell_Sales()
{
    var invoice = Invoice();

    Pay(invoice, 1, 500);
    invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
    Pay(invoice, 2, 480.75m);
    invoice.Status.Should().Be(InvoiceStatus.Paid);

    invoice.DomainEvents.Should().Equal(new PaymentRecorded(1, 7, 500), new PaymentRecorded(2, 7, 480.75m));
}
```

The integration test `Shipping_issues_an_invoice_and_a_payment_lowers_the_customer_balance` gets its second
half: record a payment through the API (with `"method": "BankTransfer"`, which also proves the enum
converter), then wait until Sales shows the customer owing nothing and their full credit available again.
That one test walks from an order through Inventory and Invoicing back to Sales.

The UI flow test gets its last step, so it now walks from a new order to a paid invoice:

```csharp
// The invoice appears by itself; the accountant records full payment.
var accountant = await SignIn("accountant@erp.local");
var invoiceRow = accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = orderNumber });
for (var attempt = 0; attempt < 15 && await invoiceRow.CountAsync() == 0; attempt++)
{
    await accountant.GotoAsync($"/invoices?search={Uri.EscapeDataString(orderNumber)}");
    await accountant.WaitForTimeoutAsync(1000);
}
await Shot(accountant, "accountant-invoices");
await invoiceRow.First.ClickAsync();
await accountant.GetByRole(AriaRole.Button, new() { Name = "Record payment" }).ClickAsync();
await accountant.GetByRole(AriaRole.Button, new() { Name = "Record", Exact = true }).ClickAsync();
await accountant.Locator(".mud-chip").Filter(new() { HasText = "Paid" }).WaitForAsync();
await Shot(accountant, "accountant-paid");

(await accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = "Balance due" }).InnerTextAsync()).Should().Contain("₱0.00");
```

It needs the order's number, read right after the rep submits: `var orderNumber = await rep.Locator("h4").First.InnerTextAsync();`.
Rename the test `From_order_to_paid_invoice_through_the_screens`, and add `"/receivables-aging"` to `pages`.

## How a request flows

```mermaid
sequenceDiagram
  participant A as Accountant
  participant V as Invoicing (RecordPaymentHandler)
  participant O as invoicing.outbox_message
  participant D as Outbox dispatcher
  participant S as Sales (PaymentRecordedHandler)
  A->>V: POST /api/invoices/17/payments { amount: 500, method: "Cash", ... }
  V->>V: invoice.RecordPayment → PartiallyPaid; raises PaymentRecorded(31, customer 1, 500)
  V->>O: INSERT payment, UPDATE invoice, INSERT outbox row (one transaction)
  D->>S: PaymentRecorded(31, 1, 500)
  S->>S: applied_payment 31 exists? no → Apply: deduct min(500, balance)
  S->>S: INSERT applied_payment 31; UPDATE customer (one transaction)
  D->>S: PaymentRecorded(31, ...) again, after a crash
  S->>S: applied_payment 31 exists → do nothing
```

## Mistakes to avoid

- Applying a payment in Sales without remembering its id. At least once becomes twice, and money vanishes.
- Giving back what was paid instead of what was deducted. An overpayment would inflate the balance.
- Deleting a payment. Void it, with a reason; the history stays.
- Voiding an invoice that has payments. Void the payments first, so Sales hears about the money.
- An aging report built from today's balances. Use the as-of date for issue dates and payment dates alike.
- Enums as numbers on the wire. Names can't be silently misread.

## Let's run it!

Restart the app. Then:

1. As the rep, create and submit an order for Beta Industries, which has only ₱500 of credit left: 10
   Widgets, ₱250. Approved on the spot. As the clerk, ship it.
2. As the manager, open **Customers**: Beta owes ₱4,750 and has ₱250 available.
3. As the accountant, open the new invoice. **Record payment**: ₱250, today, Cash. The invoice is **Paid**.
4. A moment later, as the manager: Beta owes ₱4,500 again, and has ₱500 available. Sales never saw the
   payment screen; it heard the event.
5. As the accountant, **Void** that payment, reason "Recorded on the wrong invoice". The invoice is Issued
   again, and Beta's balance goes back up to ₱4,750.
6. **Receivables aging**: Beta's ₱250 sits under "Not yet due". Change the as-of date to two months from now:
   it moves to "31–60 days". That's the report an accountant runs every month-end.

And replay the event on purpose, as in lesson 19:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
  -c "UPDATE invoicing.outbox_message SET processed_utc = NULL WHERE type LIKE '%PaymentRecorded%';" \
  -c "SELECT payment_id, amount, reversed_utc FROM sales.applied_payment;"
```

Wait two seconds and check Beta's balance: unchanged. The receipt book said "already recorded".

Then `dotnet test`. Every test passes, and the UI flow test (with `ERP_UI_URL` set) now walks the whole
business, from a rep's draft to a paid invoice, through five people's screens.

## Let's check!

1. Why does Sales lower the customer's balance itself, instead of Invoicing doing it?
2. What does the `applied_payment` table protect against, and how?
3. A customer owes ₱300 and pays ₱500. What does Sales deduct, and what does it give back if the payment is
   voided?
4. Why can't an invoice with payments be voided directly?
5. What does the aging report's as-of date change, and which two dates does it compare?
6. Why do enums travel as names?

And that's where this lesson ends. The whole business now runs through the ERP: sell, approve, reserve,
ship, invoice, get paid, and buy more, with every module keeping its own facts and every reaction delivered
reliably. What's left is making it ready for the outside world. Next time: security hardening. Good luck,
developer!

---

← [Lesson 19 — Invoicing](19-invoicing.md) · Next: [Lesson 21 — Security hardening](21-security-hardening.md) →
