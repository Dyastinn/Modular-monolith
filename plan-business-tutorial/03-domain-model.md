# Lesson 03 — The Domain Model: The Rules, in Plain C#

Good day, developer! Today is the most important lesson in the whole course. We'll write the business rules
themselves: when a customer is blocked, how much credit is left, who may sell a controlled item, when an
order needs a manager, and why nobody may approve their own order. And we'll do it with no database and no
HTTP, just plain C#, so we can prove every rule with tests that run in milliseconds. Give yourself about two
hours. Are you ready? Let's begin!

## What we'll build

| Piece | What it is |
|---|---|
| **Base types** | `Entity`, `AggregateRoot`, `DomainException`, `Roles`: shared by every module |
| **Value objects** | `Money` and `Quantity`, also shared, plus typed ids like `SalesOrderId` |
| **Sales' entities** | `Customer`, `ItemCategory`, `SalesItem` |
| **The aggregate** | `SalesOrder` with its lines, a status state machine, and the approval workflow |
| **A test project** | Seventeen tests that need no database |

## Why we build it this way

### The pain: rules scattered across handlers

Here's the obvious version. You put the checks right where the data is loaded:

```csharp
// inside some handler
if (customer.IsBlocked) throw ...;
if (customer.Balance + total > customer.CreditLimit) throw ...;
order.Status = "PendingApproval";
```

Now "submit" has the credit check, "update" copies it, and the copy slowly drifts. Someone sets
`order.Status = "Approved"` straight from a draft, because nothing stops them. And testing the credit rule
means starting a database, because the rule lives next to the query.

### The fix: put each rule on the thing it's about

| The question | Where the answer lives |
|---|---|
| May this customer order? | A method on `Customer` |
| Can this order move from Draft to Approved? | A method on `SalesOrder`, guarded by a state machine |
| Is this amount valid? | `Money` itself: it refuses to exist if it isn't |

The data can only change through those methods, because the setters are private. So the rules can't be
skipped. And since none of this touches a database, every rule can be unit-tested.

Here's the one table to remember from the whole course. Every rule you'll ever write fits in one of these
rows:

| The question | Where it lives |
|---|---|
| Is the request well-formed? (positive ids, at least one line) | A validator ([lesson 07](07-create-order.md)) |
| Is this operation allowed? (blocked, credit, status, approval) | **The domain** — this lesson |
| Load, act, save | A handler ([lesson 07](07-create-order.md)), with no business `if`s |
| Two people acting at once | The database, atomically ([lessons 09](09-submit.md) and [10](10-approve-reject-cancel.md)) |

To remember it easily: if the question is "is this allowed?", the answer lives in the domain.

### Three words you need

- A **value object** is defined only by its value, and never changes. Think of money: any ₱1,000 bill is as
  good as any other. You don't care *which* bill you have, only its value. `Money` and `Quantity` are value
  objects.
- An **entity** has an identity that stays the same while its data changes. Customer #1 is still customer
  #1 after they change their name.
- An **aggregate** is a group of objects changed as one unit, with one **root** that the outside world
  talks to. An order and its lines are one aggregate: you never change a line except through its order.

## 1. The shared building blocks

Every module's domain will need the same few pieces, so they go in `Erp.Shared.BuildingBlocks`:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/BuildingBlocks.cs
namespace Erp.Shared.BuildingBlocks;

public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct;

// A business rule said no. Becomes HTTP 422.
public class DomainException(string message) : Exception(message);
```

- **`Entity<TId>`** is anything with an identity. `TId` is a small struct like `SalesOrderId`, never a
  plain `long`; you'll see why in a moment.
- **`AggregateRoot<TId>`** marks the one entity the outside world talks to. It's empty for now. In
  [lesson 12](12-events-outbox.md) it learns to record events.
- **`DomainException`** is how a business rule says no.
- **`where TId : struct`** appears on both classes. Leave it off `AggregateRoot` and the compiler stops you
  with `CS0453`: a derived generic class must repeat its base class's constraints.

Pesos and quantities will appear in every module that touches an order, a purchase or an invoice, so they
live here too:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/Money.cs
namespace Erp.Shared.BuildingBlocks;

// Philippine pesos, never negative, always rounded to centavos.
public readonly record struct Money
{
    public static readonly Money Zero = new(0m);

    public decimal Amount { get; }

    public Money(decimal amount) =>
        Amount = amount >= 0
            ? Math.Round(amount, 2, MidpointRounding.AwayFromZero)
            : throw new DomainException("An amount of money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator *(Money price, Quantity qty) => new(price.Amount * qty.Value);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    public static Money Min(Money a, Money b) => a <= b ? a : b;
    public override string ToString() => $"₱{Amount:N2}";
}
```

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/Quantity.cs
namespace Erp.Shared.BuildingBlocks;

// Greater than zero, at most three decimal places (2.5 kg). Whether fractions are allowed at all depends on the
// item's unit, which only the item knows.
public readonly record struct Quantity
{
    public decimal Value { get; }

    public Quantity(decimal value)
    {
        if (value <= 0) throw new DomainException("Quantity must be greater than zero.");
        if (decimal.Round(value, 3) != value) throw new DomainException("Quantity allows at most three decimal places.");
        Value = value;
    }

    public bool IsWhole => decimal.Truncate(Value) == Value;

    public static Quantity operator +(Quantity a, Quantity b) => new(a.Value + b.Value);
    public static bool operator >(Quantity a, Quantity b) => a.Value > b.Value;
    public static bool operator <(Quantity a, Quantity b) => a.Value < b.Value;
    public override string ToString() => Value.ToString("0.###");
}
```

| Detail | Why |
|---|---|
| `readonly record struct` | Immutable, compared by value, and cheap: no allocation |
| The constructor throws | An invalid `Money` or `Quantity` can't exist, so nothing downstream has to check |
| `Money` rounds to centavos | ₱620.50 × 1.5 kg is ₱930.75, not ₱930.750; every total adds up the same way everywhere |
| `Quantity` is a `decimal` | A business sells 2.5 kg of wire as well as 2 pieces. Whether *this* item allows fractions is a rule on the item |

And the role names, written once so nobody ever types one:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/Roles.cs
namespace Erp.Shared.BuildingBlocks;

// The role names, defined once. A typo in a role string silently breaks an auth check, so nobody types them.
public static class Roles
{
    public const string SalesRep = "SalesRep";
    public const string SalesManager = "SalesManager";
    public const string Admin = "Admin";

    public static readonly string[] All = [SalesRep, SalesManager, Admin];
}
```

Three roles are all Sales needs. [Lesson 18](18-users-email.md) adds the warehouse, purchasing and
accounting roles, when their screens arrive.

You might wonder why all of BuildingBlocks shares one namespace, whatever the folder. Folders like
`Domain/` are for people browsing the project; one namespace means one `using` line in every module.

## 2. Typed ids

Folder: `src/Modules/Sales/Erp.Modules.Sales/Domain/`. Everything here is `internal`.

```csharp
// Domain/Ids.cs
namespace Erp.Modules.Sales.Domain;

internal readonly record struct SalesOrderId(long Value);
internal readonly record struct CustomerId(long Value);
internal readonly record struct CategoryId(long Value);
internal readonly record struct ItemId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct UserId(long Value);
```

Why not plain `long`s? Because then this compiles, and is wrong:

```csharp
order.AddLine(customerId, ...);   // a customer id where an item id belongs
```

With typed ids, the compiler refuses it. It costs one line per id type.

## 3. The customer

```csharp
// Domain/Customer.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class Customer : Entity<CustomerId>
{
    public string Name { get; private set; } = default!;
    public bool IsBlocked { get; private set; }
    public Money CreditLimit { get; private set; }
    public Money OutstandingBalance { get; private set; }   // approved orders not yet paid for

    private Customer() { }

    public static Customer Create(string name, Money creditLimit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("A customer needs a name.");
        return new Customer { Name = name.Trim(), CreditLimit = creditLimit };
    }

    public void EnsureNotBlocked()
    {
        if (IsBlocked)
            throw new DomainException($"Customer '{Name}' is blocked and cannot place orders.");
    }

    // Exposure = what they owe + what's awaiting approval + this order.
    public void EnsureWithinCreditLimit(Money orderTotal, Money otherPendingTotal)
    {
        if (OutstandingBalance + otherPendingTotal + orderTotal <= CreditLimit) return;

        var available = Math.Max(0, CreditLimit.Amount - OutstandingBalance.Amount - otherPendingTotal.Amount);
        throw new DomainException(
            $"Order total ₱{orderTotal.Amount:N2} would exceed the credit limit. Available credit: ₱{available:N2}.");
    }

    public void AddToBalance(Money amount) => OutstandingBalance += amount;

    // A cancelled order, or money received. Never below zero.
    public void DeductFromBalance(Money amount) =>
        OutstandingBalance = OutstandingBalance - Money.Min(amount, OutstandingBalance);
}
```

Look closely at `EnsureWithinCreditLimit`. It doesn't only add this order to what the customer owes; it
also adds `otherPendingTotal`, the customer's other orders still waiting for a manager. Without that, a
customer with ₱100,000 of credit could submit three ₱90,000 orders before anyone approves the first. Each
would pass on its own, and once all three were approved, they'd owe ₱270,000.

The private parameterless constructor and the `Create` factory are a pair you'll see everywhere. `Create`
checks the rules for a new customer; the private constructor is for the database to use later, in
[lesson 04](04-database.md). [Lesson 15](15-customers.md) gives customers a TIN, an address, payment
terms and a block button; until then, the seed data sets them up.

## 4. Categories and items

A **category** holds sales rules for a group of items: who may sell them, and how many one order may hold.

```csharp
// Domain/ItemCategory.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Sales rules that apply to a group of items: who may sell them, and how much per order.
internal sealed class ItemCategory : Entity<CategoryId>
{
    public string Name { get; private set; } = default!;
    public decimal MaxQuantityPerOrder { get; private set; }
    public string? RequiredRole { get; private set; }   // null = anyone who may sell

    private ItemCategory() { }

    public static ItemCategory Create(string name, decimal maxQuantityPerOrder, string? requiredRole)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("A category needs a name.");
        if (maxQuantityPerOrder <= 0) throw new DomainException("The maximum quantity per order must be greater than zero.");
        if (requiredRole is not null && !Roles.All.Contains(requiredRole))
            throw new DomainException($"'{requiredRole}' is not a role.");
        return new ItemCategory { Name = name.Trim(), MaxQuantityPerOrder = maxQuantityPerOrder, RequiredRole = requiredRole };
    }

    public void EnsureRoleAllowed(string callerRole)
    {
        if (RequiredRole is not null && callerRole != RequiredRole && callerRole != Roles.Admin)
            throw new DomainException($"Category '{Name}' requires role '{RequiredRole}'.");
    }

    public void EnsureQuantityAllowed(decimal quantityOnOrder)
    {
        if (quantityOnOrder > MaxQuantityPerOrder)
            throw new DomainException(
                $"Category '{Name}' allows at most {MaxQuantityPerOrder:0.###} per order (requested {quantityOnOrder:0.###}).");
    }
}
```

An **item**, as far as Sales is concerned, is something with a price that it may sell. Sales doesn't own the
list of items: that's the warehouse's catalog, which arrives with the Inventory module in
[lesson 16](16-item-catalog.md). Sales keeps its own copy of what it needs from the catalog (SKU, name,
unit, whether fractions are allowed, whether the item is active), plus the three things only Sales decides:
the price, the category, and whether it's on sale.

```csharp
// Domain/SalesItem.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Sales' copy of a catalog item, plus what only Sales decides: the price, the category, and whether it's for sale.
internal sealed class SalesItem : Entity<ItemId>
{
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public bool AllowsFractions { get; private set; }
    public bool IsActive { get; private set; }
    public Money UnitPrice { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public ItemCategory? Category { get; private set; }
    public bool IsForSale { get; private set; }

    private SalesItem() { }

    // A new catalog item arrives unpriced and not for sale.
    public static SalesItem FromCatalog(ItemId id, string sku, string name, string unit, bool allowsFractions, bool isActive)
    {
        var item = new SalesItem { Id = id };
        item.ApplyCatalog(sku, name, unit, allowsFractions, isActive);
        return item;
    }

    public void ApplyCatalog(string sku, string name, string unit, bool allowsFractions, bool isActive)
    {
        Sku = sku; Name = name; Unit = unit; AllowsFractions = allowsFractions; IsActive = isActive;
    }

    public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale)
    {
        if (isForSale && unitPrice == Money.Zero) throw new DomainException($"Set a price for {Sku} before putting it on sale.");
        if (isForSale && !IsActive) throw new DomainException($"{Sku} is inactive in the catalog and can't be sold.");
        UnitPrice = unitPrice;
        CategoryId = category.Id;
        Category = category;
        IsForSale = isForSale;
    }

    public ItemCategory EnsureSellable()
    {
        if (!IsActive || !IsForSale || Category is null)
            throw new DomainException($"Item {Sku} is not for sale.");
        return Category;
    }

    public void EnsureQuantityFits(Quantity quantity)
    {
        if (!AllowsFractions && !quantity.IsWhole)
            throw new DomainException($"Item {Sku} is sold in whole {Unit}; {quantity} is not allowed.");
    }
}
```

Until lesson 16, the development seed puts the items in directly. The rules don't care where an item came
from.

## 5. The sales order

An order moves through a small, fixed set of statuses. Every legal move is written down in one place:

```mermaid
stateDiagram-v2
  [*] --> Draft
  Draft --> PendingApproval: Submit
  Draft --> Cancelled: Cancel
  PendingApproval --> Approved: ≤ ₱50,000 (automatic) or manager approves
  PendingApproval --> Rejected: Reject (with a reason)
  PendingApproval --> Cancelled: Cancel
  Approved --> Cancelled: Cancel (reverses the charge)
```

```csharp
// Domain/SalesOrder.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Stored as numbers: never renumber.
internal enum SalesOrderStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Cancelled = 3,
    Rejected = 4
}

internal static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> Allowed = new()
    {
        [SalesOrderStatus.Draft]           = [SalesOrderStatus.PendingApproval, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.PendingApproval] = [SalesOrderStatus.Approved, SalesOrderStatus.Rejected, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.Approved]        = [SalesOrderStatus.Cancelled],
        [SalesOrderStatus.Cancelled]       = [],
        [SalesOrderStatus.Rejected]        = [],
    };

    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) => Allowed[from].Contains(to);
}

internal static class ApprovalPolicy
{
    // Orders above this need a manager; at or below it they're approved on submit.
    public static readonly Money Threshold = new(50_000m);
}

// The line keeps the item's name and unit as they were when ordered, like a printed order would.
internal sealed class SalesOrderLine
{
    public ItemId ItemId { get; private set; }
    public string ItemName { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private SalesOrderLine() { }

    public SalesOrderLine(ItemId itemId, string itemName, string unit, Quantity quantity, Money unitPrice)
    { ItemId = itemId; ItemName = itemName; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; }

    public void IncreaseQuantity(Quantity extra) => Quantity += extra;
}

internal sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public string OrderNumber { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public UserId? ApprovedBy { get; private set; }   // null = approved automatically
    public DateTime? ApprovedUtc { get; private set; }
    public UserId? RejectedBy { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;
    public bool HoldsStock => Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved;

    private SalesOrder() { }

    public static SalesOrder Create(string orderNumber, CustomerId customerId, UserId createdBy, DateTime nowUtc) => new()
    {
        OrderNumber = orderNumber,
        CustomerId = customerId,
        CreatedBy = createdBy,
        CreatedUtc = nowUtc,
        Status = SalesOrderStatus.Draft
    };

    // Replaces every line. The category caps count across the whole order, so they're checked on the full set.
    public void SetLines(IReadOnlyList<(SalesItem Item, Quantity Quantity)> lines, string callerRole)
    {
        EnsureDraft();
        if (lines.Count == 0) throw new DomainException("An order needs at least one line.");
        _lines.Clear();

        var perCategory = new Dictionary<CategoryId, decimal>();
        foreach (var (item, quantity) in lines)
        {
            var category = item.EnsureSellable();
            category.EnsureRoleAllowed(callerRole);
            item.EnsureQuantityFits(quantity);
            perCategory[category.Id] = perCategory.GetValueOrDefault(category.Id) + quantity.Value;
            category.EnsureQuantityAllowed(perCategory[category.Id]);

            var existing = _lines.FirstOrDefault(l => l.ItemId == item.Id);
            if (existing is not null) existing.IncreaseQuantity(quantity);
            else _lines.Add(new SalesOrderLine(item.Id, item.Name, item.Unit, quantity, item.UnitPrice));
        }
    }

    // otherPendingTotal: the customer's other submitted-but-unapproved orders, which already count against credit.
    public void Submit(Customer customer, Money otherPendingTotal, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an order with no lines.");
        customer.EnsureNotBlocked();
        customer.EnsureWithinCreditLimit(Total, otherPendingTotal);

        Transition(SalesOrderStatus.PendingApproval);
        SubmittedUtc = nowUtc;

        if (!RequiresApproval)
            MarkApproved(approver: null, customer, nowUtc);
    }

    public void Approve(UserId approver, Customer customer, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        if (approver == CreatedBy)
            throw new DomainException("An order can't be approved by the person who created it.");
        MarkApproved(approver, customer, nowUtc);
    }

    // Returns the lines whose reserved stock must be released.
    public IReadOnlyList<SalesOrderLine> Reject(UserId rejectedBy, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection needs a reason.");
        Transition(SalesOrderStatus.Rejected);
        RejectedBy = rejectedBy;
        RejectedUtc = nowUtc;
        RejectionReason = reason.Trim();
        return _lines;
    }

    // Returns the lines whose reserved stock must be released (none for a draft).
    public IReadOnlyList<SalesOrderLine> Cancel(Customer customer)
    {
        EnsureBelongsTo(customer);
        var heldStock = HoldsStock;
        var wasCharged = Status == SalesOrderStatus.Approved;

        Transition(SalesOrderStatus.Cancelled);

        if (wasCharged)
            customer.DeductFromBalance(Total);
        return heldStock ? _lines : [];
    }

    private void MarkApproved(UserId? approver, Customer customer, DateTime nowUtc)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedBy = approver;
        ApprovedUtc = nowUtc;
        customer.AddToBalance(Total);
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
    }

    private void EnsureBelongsTo(Customer customer)
    {
        if (customer.Id != CustomerId)
            throw new InvalidOperationException($"Customer {customer.Id.Value} does not own order {OrderNumber}.");
    }

    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException($"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}
```

That's a lot of code, so let's walk through what each part protects:

- **`Total` is computed** from the lines. It can never disagree with them, because it's never stored.
- **`SetLines` replaces the whole list** and checks every rule on the complete set: the item is for sale,
  the caller's role may sell its category, whole-unit items get whole quantities, and the category cap
  counts across all lines, so five Controlled items split over two lines are still five.
- **The same item twice becomes one line.** Adding 2 Widgets and then 3 Widgets is one line of 5.
- **The line keeps the item's name and unit** as they were when ordered. Rename the item next year, and
  this order still says what was ordered.
- **`Submit`** checks the customer and moves the order to `PendingApproval`. If the order is ₱50,000 or
  less, it approves it on the spot. Small orders don't wait for a manager.
- **`Approve`** refuses the person who created the order. Businesses call this *segregation of duties*:
  nobody signs off on their own big sale.
- **Approving charges the customer** (`AddToBalance`), and **cancelling an approved order takes it back**.
  The domain does both, so no handler can forget one.
- **`Reject` and `Cancel` return the lines to release.** The order knows whether it was holding stock; the
  handler just does what it's told ([lesson 10](10-approve-reject-cancel.md)). No business `if` leaks out.
- **The time comes in as a parameter** (`nowUtc`) instead of `DateTime.UtcNow`, so tests can control it.
- `EnsureBelongsTo` throws `InvalidOperationException`, not `DomainException`. Passing the wrong customer is
  a programming bug, not a business rule a user can break.

Clear so far? Good. Now let's prove all of it.

## 6. The unit tests

```bash
dotnet new xunit -o tests/Erp.Modules.Sales.UnitTests
dotnet sln add tests/Erp.Modules.Sales.UnitTests
```

The template's `.csproj` names a version on each package, which central package management forbids. Open it,
delete every `<PackageReference>` line, and delete the generated `UnitTest1.cs`. Then add the packages our
way:

```bash
dotnet add tests/Erp.Modules.Sales.UnitTests reference src/Modules/Sales/Erp.Modules.Sales
dotnet add tests/Erp.Modules.Sales.UnitTests package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add tests/Erp.Modules.Sales.UnitTests package xunit --version 2.9.3
dotnet add tests/Erp.Modules.Sales.UnitTests package xunit.runner.visualstudio --version 3.1.4
dotnet add tests/Erp.Modules.Sales.UnitTests package AwesomeAssertions --version 9.6.0
```

| Package | What it's for | License |
|---|---|---|
| `xunit`, `xunit.runner.visualstudio` | The test framework, and what lets `dotnet test` find the tests | Apache-2.0 |
| `Microsoft.NET.Test.Sdk` | The plumbing between `dotnet test` and the framework | MIT |
| `AwesomeAssertions` | Readable checks: `total.Should().Be(...)`. A free fork of FluentAssertions, whose version 8 needs a paid license | Apache-2.0 |

The domain is `internal`, so let the test project in. In `Erp.Modules.Sales.csproj`, add a line next to
the `Erp.Api` one:

```xml
<InternalsVisibleTo Include="Erp.Modules.Sales.UnitTests" />
```

```csharp
// tests/Erp.Modules.Sales.UnitTests/DomainTests.cs
using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

internal static class Make
{
    public static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly UserId Rep = new(1);
    public static readonly UserId Manager = new(2);

    public static readonly ItemCategory General = ItemCategory.Create("General", 1000, null);
    public static readonly ItemCategory Controlled = ItemCategory.Create("Controlled", 5, Roles.SalesManager);

    public static Customer Customer(decimal limit = 1_000_000, decimal balance = 0)
    {
        var customer = Domain.Customer.Create("Acme", new Money(limit));
        if (balance > 0) customer.AddToBalance(new Money(balance));
        return customer;
    }

    public static SalesItem Item(long id, decimal price, ItemCategory? category = null, bool fractions = false, string unit = "pc")
    {
        var item = SalesItem.FromCatalog(new ItemId(id), $"SKU-{id}", $"Item {id}", unit, fractions, isActive: true);
        item.SetPricing(new Money(price), category ?? General, isForSale: true);
        return item;
    }

    public static SalesOrder Order(Customer customer, decimal unitPrice, decimal qty = 1)
    {
        var order = SalesOrder.Create("SO-1", customer.Id, Rep, Now);
        order.SetLines([(Item(1, unitPrice), new Quantity(qty))], Roles.SalesRep);
        return order;
    }
}

public class CustomerRuleTests
{
    [Fact]
    public void Order_over_available_credit_is_rejected() =>
        Make.Customer(limit: 5000, balance: 4500).Invoking(c => c.EnsureWithinCreditLimit(new Money(600), Money.Zero))
            .Should().Throw<DomainException>().WithMessage("*credit limit*");

    [Fact]
    public void Orders_awaiting_approval_count_against_credit() =>
        Make.Customer(limit: 200_000).Invoking(c => c.EnsureWithinCreditLimit(new Money(75_000), new Money(150_000)))
            .Should().Throw<DomainException>().WithMessage("*Available credit: ₱50,000.00*");

    [Fact]
    public void A_balance_never_goes_below_zero()
    {
        var customer = Make.Customer(balance: 300);
        customer.DeductFromBalance(new Money(500));
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }
}

public class OrderLineTests
{
    [Fact]
    public void Controlled_items_need_a_manager()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);
        var kit = Make.Item(3, 500, Make.Controlled);

        order.Invoking(o => o.SetLines([(kit, new Quantity(1))], Roles.SalesRep)).Should().Throw<DomainException>();
        order.Invoking(o => o.SetLines([(kit, new Quantity(1))], Roles.SalesManager)).Should().NotThrow();
    }

    [Fact]
    public void Category_cap_counts_across_the_whole_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Manager, Make.Now);
        var kitA = Make.Item(3, 500, Make.Controlled);
        var kitB = Make.Item(4, 500, Make.Controlled);

        order.Invoking(o => o.SetLines([(kitA, new Quantity(3)), (kitB, new Quantity(3))], Roles.SalesManager))
            .Should().Throw<DomainException>().WithMessage("*at most 5*");
    }

    [Fact]
    public void Whole_unit_items_refuse_fractions_but_bulk_items_allow_them()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);

        order.Invoking(o => o.SetLines([(Make.Item(1, 25), new Quantity(1.5m))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*whole*");
        order.SetLines([(Make.Item(5, 620.50m, fractions: true, unit: "kg"), new Quantity(1.5m))], Roles.SalesRep);
        order.Total.Should().Be(new Money(930.75m));
    }

    [Fact]
    public void An_item_not_for_sale_cannot_be_ordered()
    {
        var unpriced = SalesItem.FromCatalog(new ItemId(9), "NEW-1", "New", "pc", false, isActive: true);
        SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now)
            .Invoking(o => o.SetLines([(unpriced, new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*not for sale*");
    }

    [Fact]
    public void The_same_item_twice_becomes_one_line()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);
        var widget = Make.Item(1, 25);
        order.SetLines([(widget, new Quantity(2)), (widget, new Quantity(3))], Roles.SalesRep);
        order.Lines.Should().ContainSingle().Which.Quantity.Should().Be(new Quantity(5));
    }
}

public class SubmitAndApproveTests
{
    [Fact]
    public void Small_order_is_approved_automatically_and_charged()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25, qty: 2);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
        order.ApprovedBy.Should().BeNull();
        customer.OutstandingBalance.Should().Be(new Money(50));
    }

    [Fact]
    public void High_value_order_waits_for_a_manager()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Creator_cannot_approve_their_own_order()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.Approve(Make.Rep, customer, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*person who created it*");
        order.Approve(Make.Manager, customer, Make.Now);
        customer.OutstandingBalance.Should().Be(new Money(60_000));
    }

    [Fact]
    public void Lines_are_frozen_after_submitting()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.SetLines([(Make.Item(2, 40), new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*draft*");
    }
}

public class RejectAndCancelTests
{
    [Fact]
    public void Rejecting_needs_a_reason_and_releases_the_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.Reject(Make.Manager, " ", Make.Now)).Should().Throw<DomainException>();
        order.Reject(Make.Manager, "Customer asked for a quote first", Make.Now).Should().HaveCount(1);
        order.Status.Should().Be(SalesOrderStatus.Rejected);
    }

    [Fact]
    public void Cancelling_an_approved_order_reverses_the_charge_and_releases_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25, qty: 2);
        order.Submit(customer, Money.Zero, Make.Now);   // auto-approved: charged ₱50

        order.Cancel(customer).Should().HaveCount(1);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Cancelling_a_draft_releases_nothing()
    {
        var customer = Make.Customer();
        Make.Order(customer, unitPrice: 25).Cancel(customer).Should().BeEmpty();
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Quantity_must_be_positive_with_at_most_three_decimals()
    {
        FluentActions.Invoking(() => new Quantity(0)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Quantity(1.2345m)).Should().Throw<DomainException>();
        new Quantity(1.234m).Value.Should().Be(1.234m);
    }

    [Fact]
    public void Money_cannot_be_negative_and_rounds_to_centavos()
    {
        FluentActions.Invoking(() => new Money(-1)).Should().Throw<DomainException>();
        (new Money(620.50m) * new Quantity(1.333m)).Should().Be(new Money(827.13m));
    }
}
```

`Make` builds the objects the tests need, so each test only states what's special about it. A new customer
has an id of 0 until the database gives it one, so `Make.Order` uses `customer.Id` rather than inventing
one. Each test reads like a sentence from the business rules; when a rule changes, the test that names it is
the first thing you update.

## Mistakes to avoid

- Public setters "for convenience". One `order.Status = Approved` anywhere, and the state machine is just
  decoration.
- Storing `Total`. A stored total can disagree with the lines; a computed one can't.
- Checking the category cap per line. Five Controlled items over two lines must still be five.
- `DateTime.UtcNow` inside the domain. You can't test it. Pass the time in.
- Throwing `DomainException` for programmer mistakes. It becomes a 422 the user sees, when a bug should be
  a loud 500.
- Vague method names. Use one method per business verb (`Submit`, `Approve`, `Reject`, `Cancel`), named in
  the business's own words.

## Let's run it!

```bash
dotnet test
```

```
Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17
```

Seventeen business rules, proven in well under a second, with no database in sight. That's the reward for
keeping the domain pure.

## Let's check!

1. Why does `Customer.EnsureWithinCreditLimit` take `otherPendingTotal`? Describe the bug without it.
2. Where is "an order over ₱50,000 needs a manager" enforced, and how does a small order skip the manager?
3. Why does `Reject` return the order's lines, instead of the handler deciding what to release?
4. What stops a handler from setting `order.Status = SalesOrderStatus.Approved` on a draft?
5. Why does `SetLines` check the category cap across the whole order, not line by line?
6. Why does an order line keep the item's name, instead of looking it up when needed?

And that's where this lesson ends. You now have every rule of a sales order, in plain C#, with tests to
prove them. Next time, we give the module its memory: a PostgreSQL database. Good luck, developer!

---

← [Lesson 02 — Mediator](02-mediator.md) · Next: [Lesson 04 — Database](04-database.md) →
