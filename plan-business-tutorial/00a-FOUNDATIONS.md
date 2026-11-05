# Foundations — The C# You Need Before Lesson 01

Good day, developer! Before we build anything, let's make sure the everyday C# words won't trip you up.
This page isn't about our architecture; it's about the building blocks the architecture is made of. You
don't need to memorize it. You need to recognize these things when they show up, and come back here when
one doesn't make sense. Every senior developer was confused by all of this once. Are you ready? Let's
begin!

## 0. How to read a code example without panicking

Don't try to understand every character at once. Instead:

1. **Read the method names first.** They usually say what happens: `Create`, `AddLine`, `Submit`.
2. **Skip the punctuation on the first pass.** `<T>`, `=>`, `?`, `async`: get the gist first.
3. **Then come back here** and decode the symbols.

Understanding what the code is trying to do comes before understanding every symbol. Always.

## 1. What are C# and .NET?

**C#** is the language we write in: the words and the grammar. **.NET** is the platform that runs it. It
compiles your code and comes with a huge library of ready-made tools for the web, databases and more. We use
**.NET 10**.

To **compile** means to translate the C# you wrote into something the computer can run. If the compiler
finds a mistake, you get an error instead of a program. That's good news: a compile error is a bug caught
before any user met it. This course sets things up on purpose so that certain mistakes, like one module
reaching into another, become compile errors. When a lesson says "the compiler stops you", that's what it
means.

## 2. Classes, objects and `new`

A **class** is a blueprint. `class Dog { ... }` describes what a dog is and what it can do. An **object** is
one actual thing built from that blueprint, made with `new`. The class is the cookie cutter; each object is
a cookie.

```csharp
public class Dog
{
    public string Name { get; set; } = "";   // data every dog has
    public void Bark() { }                   // something every dog can do
}

var rex = new Dog();   // one actual dog
rex.Name = "Rex";
rex.Bark();
```

A **property** (`Name`, `Status`, `Total`) is data an object holds. A **method** (`Bark()`, `Submit()`) is
something it can do. The dot means "reach into this object": `rex.Name`, `order.Submit(...)`.

### Why we hide setters

```csharp
public string Status { get; set; }           // anyone can read it, and anyone can change it
public string Status { get; private set; }   // anyone can read it, only this class can change it
```

That `private set` matters a lot in this course. It's how a `SalesOrder` protects its rules: other code can
look at the status, but can only change it by calling a method like `Submit(...)`, which checks the rules
first. [Lesson 03](03-domain-model.md) is built on it.

## 3. Interfaces: a promise, not the real thing

An **interface** promises what something can do, without saying how. Think of a wall outlet: it promises
power through two slots. The lamp doesn't care which power plant is behind the wall.

```csharp
public interface IClock          // the I prefix is a C# convention for interfaces
{
    DateTime UtcNow { get; }      // "I promise you can ask me the time"
}
```

There's no code inside an interface, only the shape. A real class **implements** it:

```csharp
public sealed class SystemClock : IClock   // ": IClock" means "I keep that promise"
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

Why care? Because code that only knows `IClock` works with *any* clock. In the app, it gets the real one. In
a test, it gets a clock stopped at 9:00 a.m. on a Monday, so a time-based rule can be tested exactly. When you
see an `I` in front of a type name (`IClock`, `ICurrentUser`, `IStockReservation`), read it as "a promise".

## 4. Dependency injection: "hand me what I need"

Look at this class from lesson 08:

```csharp
internal sealed class CancelSalesOrderHandler(SalesDbContext db, IStockReservation stock)
{
    // db and stock are handed to this class from outside
}
```

The handler needs a database context and a way to release stock. It doesn't create them with `new`; it
says "whoever creates me, hand me these". That handing-over is **dependency injection**, or DI.

Those parentheses right after the class name are a **primary constructor**: modern shorthand for "to build
one of these, you must supply a `db` and a `stock`, and I'll keep them". The long way says the same thing:

```csharp
internal sealed class CancelSalesOrderHandler
{
    private readonly SalesDbContext _db;
    private readonly IStockReservation _stock;

    public CancelSalesOrderHandler(SalesDbContext db, IStockReservation stock)
    {
        _db = db;
        _stock = stock;
    }
}
```

Think of a chef who doesn't own a farm. They ask for eggs and flour, and the kitchen supplies them. The chef
cooks the same way whichever farm the eggs came from. In the app, the kitchen supplies the real Inventory
service; in a test, it can supply something else. The kitchen's supply list is written once, at startup,
in `Program.cs` and each module's `AddXxxModule` method. That place is called the **composition root**.

## 5. `async` and `await`: "this takes a moment, don't freeze"

Talking to a database takes time: milliseconds, but ages for a computer. Methods that wait like that are
marked **`async`**, and return a **`Task`** or a **`ValueTask`**: a ticket for a result that arrives a little
later. **`await`** means "wait for it here, but let other work run meanwhile".

```csharp
var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
```

Rules of thumb for reading our code:

- `Task<Order>` or `ValueTask<Order>`: "an `Order`, arriving a moment later". Our handlers return
  `ValueTask`, a lighter ticket for results that are often ready straight away.
- `await x`: "wait for `x`, then give me what's inside".
- A method containing `await` is marked `async`. That's just the rule.
- **`CancellationToken ct`** appears everywhere. It's a "stop early, the caller gave up" signal. Just pass
  it along.

## 6. Generics: the `<T>` thing

Sometimes code should work with any type. `T` is a placeholder for "whatever type you plug in". A
cardboard box doesn't care what's inside: `Box<Shoe>` and `Box<Book>` are the same box design.

```csharp
internal sealed class CancelSalesOrderHandler : ICommandHandler<CancelSalesOrderCommand>
internal sealed class SearchSalesOrdersHandler : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
```

Read the second one as "the handler for a `SearchSalesOrdersQuery`, which gives back a page of order
summaries". You'll rarely write generic code yourself; you'll mostly fill in the blanks.

The `<T>` you'll see most is a list:

- **`List<SalesOrderLine>`**: a list you can add to and remove from.
- **`IReadOnlyList<SalesOrderLine>`**: a list you can look at but not change. `SalesOrder` shows its lines
  this way on purpose. Other code can read them, but can only add one through `AddLine(...)`, which checks
  the rules. It's the `private set` idea, applied to a list.

You'll also spot `in` and `out` on some generic interfaces. They're fine print that lets the compiler
accept related types in more places. You never need to write them in this course.

## 7. `class`, `record`, `struct`

- **`class`**: the normal kind. Objects with an identity and behavior. `SalesOrder` is a class.
- **`record`**: made for holding data, with value equality and easy copying built in. Commands, queries and
  results are records: `record CancelSalesOrderCommand(long OrderId)`.
- **`struct`**: a lightweight value, for tiny things. Our ids are structs:
  `readonly record struct SalesOrderId(long Value)`, a small, unchangeable wrapper around a number.

For reading purposes, `record` and `struct` here mostly mean "a small data holder". Don't overthink it.

## 8. Enums: a fixed list of names

```csharp
internal enum SalesOrderStatus { Draft, PendingApproval, Approved, Cancelled, Rejected }
```

A `SalesOrderStatus` can only ever be one of those five. You can't set it to `"draftt"` by accident; the
compiler won't allow it. That's why an order's status is an enum and not a string. And those five really are
an order's whole life; you'll build each step.

## 9. Exceptions: "stop, something's wrong"

When code reaches a point it mustn't go past, it **throws** an exception: it stops and jumps out of the
current work.

```csharp
if (Status != SalesOrderStatus.Draft)
    throw new DomainException("Lines can only be changed on a draft order.");
```

Code further up can **catch** it and decide what to do. In this course, a broken business rule throws a
`DomainException`, and the API turns it into a clear "422" answer for the caller. That's the order refusing
to do something illegal, which is exactly what we want.

## 10. Symbols you'll keep seeing

| Symbol | Name | Read it as |
|---|---|---|
| `=>` | fat arrow | "produces". `o => o.Id` means "from `o`, take `Id`" |
| `?` after a type | nullable | "might be nothing". `SalesOrder?` is "an order, or `null`" |
| `??` | null-coalescing | "or else". `a ?? b` means "`a`, but if it's nothing, `b`" |
| `?? throw` | | "or else stop here". Common after a lookup that must find something |
| `!` after a value | null-forgiving | "trust me, this isn't nothing". A human vouches, not the compiler |
| `_lines` | leading underscore | a private field, internal to its class |
| `//` | comment | a note for humans; the computer ignores it |
| `public` / `internal` / `private` | access | anyone / this project only / this class only |
| `sealed` | no subclasses | "nothing builds on this class". Most of ours are sealed |
| `var` | inferred type | "work out the type for me". `var x = 5;` makes `x` an `int` |

About that `?` and `!`: modern .NET projects turn on **nullable reference types**. The compiler tracks which
values might be `null` and warns you *before* the classic crash at runtime. `SalesOrder?` says "handle the
nothing case"; plain `SalesOrder` says "this is never nothing". The `!` is the escape hatch for places where
you know something the compiler can't.

## 11. What are a database and SQL?

A **database** keeps data after the program stops. We use **PostgreSQL**, a free, open-source database. Data
lives in **tables**, much like spreadsheets: a `sales_order` table has columns (`id`, `customer_id`,
`status`) and one row per order. **SQL** is the language for talking to it: "give me every approved order",
"insert this row".

We talk to PostgreSQL in two ways, and [lesson 04](04-database.md) and [lesson 10](10-reads.md) explain why:

- **EF Core** saves our C# objects for us. Every change to data goes through it.
- **Dapper** runs a SQL query and turns the rows into C# objects. Every screen's data comes through it.

Don't worry about writing SQL yet; the lessons show every line of it.

## 12. When you feel lost

If a lesson goes over your head, that's normal, and it isn't a sign you can't do this. Here's what works:

1. Read it once for the big idea, skipping the details.
2. Try the "Let's check!" questions at the end. They show you exactly which part didn't land.
3. Read it a second time. It'll be much easier.
4. If one word blocks you, look it up in the [glossary](GLOSSARY.md) or on this page.

Nobody learns this in one pass. Seniors just had more passes. That's the whole difference.

And that's where this page ends. Go back to [Start here](00-START-HERE.md) for the plan, then begin with
[lesson 01](01-solution-setup.md). Good luck, developer!

---

← [Start here](00-START-HERE.md) · Next: [Lesson 01 — Solution setup](01-solution-setup.md) →
