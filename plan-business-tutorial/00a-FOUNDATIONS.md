# Foundations — The Stuff You Need *Before* Phase 1

> 👨‍🏫 **A note from your senior:** If any phase ever uses a word and your brain goes "wait, what's
> that?", come back here first. There is *no shame* in not knowing these yet — every single senior dev
> was confused by all of this once. Read this slowly. You don't need to memorize it; you need to be
> able to *recognize* these things when they show up.

This file explains the plain C# and programming ideas the phases lean on. It is **not** about our
architecture — it's about the building blocks the architecture is made of.

---

## 0. How to read code examples without panicking

When you see a code block, don't try to understand every character at once. Do this instead:

1. **Read the comments first** (the lines starting with `//`). They tell you the *intent*.
2. **Find the method names** — they usually say what happens (`Create`, `AddLine`, `Submit`).
3. **Ignore the punctuation** on the first pass. `<T>`, `=>`, `?`, `async` — skip them, get the gist.
4. **Then** go back and decode the symbols using this file.

Understanding *what code is trying to do* comes before understanding *every symbol*. Always.

---

## 1. What is C#? What is .NET?

- **C#** ("C-sharp") is the programming *language* we write in. It's the words and grammar.
- **.NET** is the *platform* that runs C# — it compiles your code and provides a huge library of
  ready-made tools (for web, database, etc.). We're using **.NET 10**.
- **Compile** means "translate the C# you wrote into something the computer can actually run." If your
  code has a mistake the compiler can catch, it *won't compile* — you get an error instead of a running
  program. **A compile error is your friend**: it caught a bug before your users did.

> 💡 In this project we deliberately set things up so that certain *mistakes* cause compile errors. When
> the plan says "the compiler stops you," that's this: the translation step refuses, and you fix it
> before it ever runs.

---

## 2. Classes, objects, and `new`

- A **class** is a blueprint. `class SalesOrder { ... }` describes what a sales order *is* and what it can
  *do*.
- An **object** (also called an "instance") is one actual thing built from that blueprint. You make one
  with the keyword `new`: `var dog = new Dog();`
- **Analogy:** the class is the cookie *cutter*; each object is a *cookie* you stamp out with it.

```csharp
public class Dog          // the blueprint
{
    public string Name;   // a piece of data every dog has
    public void Bark() { /* ... */ }   // something every dog can do
}

var rex = new Dog();      // rex is one actual dog object
rex.Name = "Rex";
rex.Bark();               // call the Bark method on rex
```

### Properties, fields, methods

- A **field** or **property** is a piece of *data* an object holds (`Name`, `Status`, `Total`).
- A **method** is an *action* an object can perform (`Bark()`, `Submit()`, `AddLine()`).
- The dot `.` means "reach into this object and get/do this." `rex.Name`, `order.Submit()`.

### Getters, setters, and why we hide setters

```csharp
public string Status { get; set; }        // anyone can READ and WRITE Status
public string Status { get; private set; } // anyone can READ, but only THIS class can WRITE
```

That `private set` is a **huge deal in this project.** It's how the `SalesOrder` protects its rules:
outside code can *look* at the status, but can only *change* it by calling a method like `Submit()` —
which checks the rules first. (This is the whole "rich vs. anemic" idea, and
[Phase 5](05-domain-model.md) is built on it.)

---

## 3. Interfaces — the single most important idea to get

An **interface** is a *promise about what methods something has*, without saying *how* they work.

Think of a wall outlet. The outlet is an **interface**: "I promise to provide power through these two
slots." A lamp, a phone charger, a laptop — they all just need a plug that fits. They don't care what
power plant made the electricity. You can swap the power source behind the wall and the lamp never
knows.

```csharp
public interface ISalesOrderRepository   // the "shape" — note the I prefix, a C# convention
{
    Task AddAsync(SalesOrder order);          // "I promise: you can call AddAsync and pass a SalesOrder"
    Task<SalesOrder?> GetAsync(SalesOrderId id); // "I promise: call GetAsync and get a SalesOrder back"
}
```

An interface has **no actual code inside the methods** — just their names and shapes. Somewhere else, a
real class *implements* it (fills in the actual code):

```csharp
public class SalesOrderRepository : ISalesOrderRepository   // ": ISalesOrderRepository" = "I implement it"
{
    public async Task AddAsync(SalesOrder order) { /* real database code here */ }
    public async Task<SalesOrder?> GetAsync(SalesOrderId id) { /* real database code here */ }
}
```

**Why do we care so much?** Because code that depends on the *interface* (`ISalesOrderRepository`) doesn't
know or care about the real database code. That's what lets the `Domain` stay pure, and what lets us
swap a real database for a fake one in tests. **When you see an `I` in front of a type name
(`ISender`, `ISalesOrderRepository`, `IStockReservation`), it's almost always an interface — a promise, not the
real thing.**

---

## 4. Dependency Injection (DI) — "give me what I need, I won't go get it"

Look at this class:

```csharp
public class CreateSalesOrderHandler(ISalesOrderRepository orders, ICustomerRepository customers)
{
    // orders and customers are handed TO this class from outside.
}
```

The handler *needs* a repository to do its job. But instead of building one itself
(`new SalesOrderRepository(...)`), it says "someone hand me one that fits the `ISalesOrderRepository` shape."
That handing-over is **Dependency Injection**.

### The parentheses after the class name

`class CreateSalesOrderHandler(ISalesOrderRepository orders, ...)` looks odd the first time. Those
parentheses are a **primary constructor** — modern C# shorthand for "to build one of these, you must supply
an `orders` and a `customers`, and I'll keep them as fields." Every method in the class can then just use
`orders` by name. The long-hand version says exactly the same thing:

```csharp
public class CreateSalesOrderHandler          // the old way — same meaning, four times the typing
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerRepository _customers;

    public CreateSalesOrderHandler(ISalesOrderRepository orders, ICustomerRepository customers)
    {
        _orders = orders;
        _customers = customers;
    }
}
```

Nearly every handler, repository, and service in this course uses the short form. When you see parentheses
right after a class name, read them as **"these get handed to me when I'm created."**

**Analogy:** you're a chef. You don't own a farm. You just say "I need eggs and flour," and the kitchen
supplies them. You cook the same way whether the eggs came from farm A or farm B. DI is the kitchen
supplying your ingredients.

**Why it matters:** in real code the kitchen supplies the *real* repository; in tests the kitchen
supplies a *fake* one. The handler doesn't change. This is why DI shows up everywhere in this project.

> The place that decides "when someone asks for `ISalesOrderRepository`, give them a real
> `SalesOrderRepository`" is called the **composition root** — it lives in `Program.cs`. Think of it as the
> kitchen's supply list, written down once at startup.

---

## 5. `async` and `await` — "this might take a while, don't freeze"

Talking to a database or a website takes *time* (milliseconds, but that's ages for a computer). We don't
want the whole program to freeze while waiting. So those methods are marked **`async`** and return a
**`Task`** (a "this will be done later" ticket). You use **`await`** to say "wait for this to finish,
but let other work happen meanwhile."

```csharp
public async Task<SalesOrder?> GetAsync(SalesOrderId id)   // async, returns a Task (a future result)
{
    var order = await database.LoadOrder(id);    // await = "pause here until it's loaded"
    return order;
}
```

**Practical rules for reading our code:**
- `Task<Something>` = "a Something, but it'll arrive a moment later."
- `await x` = "wait for x to be ready, then give me the value inside."
- If a method has `await` in it, it's marked `async`. That's just the rule.
- **`CancellationToken ct`** you'll see passed around everywhere — it's a "stop early if the user gave
  up / the request was cancelled" signal. You can mostly ignore it while reading; just pass it along.

You don't need to deeply understand *how* async works to follow this project. Just read `await foo()` as
"do foo and wait for the answer."

---

## 6. Generics — the `<T>` thing

Sometimes code should work with *any* type. `T` is a placeholder for "whatever type you plug in."

**Analogy:** a cardboard box doesn't care what's inside. A "box of `T`" can be a box of shoes
(`Box<Shoe>`) or a box of books (`Box<Book>`). Same box design, different contents.

```csharp
public interface IRequestHandler<TRequest, TResponse>   // works for ANY request + response types
```

When you see `<TRequest, TResponse>` or `<T>`, read it as "this is a fill-in-the-blank type." When it's
*used*, the blanks get filled: `IRequestHandler<CreateSalesOrderCommand, SalesOrderId>` means "the handler
for a `CreateSalesOrderCommand` that gives back a `SalesOrderId`." You don't need to write generic code in
this project much — you mostly *fill in the blanks*.

**The `<T>` you'll see most is a *collection* — a list of things:**

- **`List<SalesOrderLine>`** = "a list of order lines you can add to and remove from." (A `List<T>` is the
  everyday resizable list.)
- **`IReadOnlyList<SalesOrderLine>`** = "a list you can *look at* but not change." The `SalesOrder` exposes
  its lines as `IReadOnlyList<...>` on purpose: outside code can read the lines, but can only *add* one by
  calling `AddLine(...)` — which checks the rules first. It's the same "protect your own data" idea as
  `private set` from §2, applied to a list. When you see `IReadOnlyList<T>`, read it as "a look-but-don't-
  touch list."

You won't build these types; you'll just *read* and *loop over* them (`foreach (var line in order.Lines)`).

---

## 7. `record`, `struct`, `class` — three ways to make a type

You'll see all three. Rough differences, enough to read the code:

- **`class`** — the normal one. Objects with identity and behavior. `Order` is a class.
- **`record`** — a class optimized for *holding data*, with handy built-in features (easy equality,
  easy copying). We use records for commands, queries, and DTOs: `record CreateOrderCommand(...)`.
- **`struct`** — a lightweight value type, good for tiny things. Our strongly-typed IDs are structs:
  `readonly record struct SalesOrderId(long Value)` — a tiny, unchangeable wrapper around a number.

For reading purposes: **`record` and `struct` here mostly mean "a small data holder."** Don't overthink
it yet.

---

## 8. Enums — a fixed list of named options

An **enum** is a type that can only be one of a set list of named values.

```csharp
public enum SalesOrderStatus { Draft, PendingApproval, Approved, Cancelled, Rejected }
```

Now `SalesOrderStatus` can *only* be one of those five. You can't set it to `"Banana"` or `"draftt"` (typo) —
the compiler won't allow it. That's why [Phase 5](05-domain-model.md) gives an order's status an enum
instead of a plain string: strings let typos and nonsense in; enums don't. (Those five names really are the
whole lifecycle of an order in this system — you'll meet each one as we build it.)

---

## 9. Exceptions — "stop, something's wrong"

When code hits a situation it can't or shouldn't continue past, it **throws an exception** — it
basically shouts "NOPE!" and stops, jumping out of the current work.

```csharp
if (Status != SalesOrderStatus.Draft)
    throw new DomainException("Only draft orders can be submitted.");
```

- **`throw`** = raise the alarm and stop here.
- The `DomainException` carries a message explaining what went wrong.
- Higher-up code can **catch** exceptions and decide what to do (e.g., turn it into a "400 Bad Request"
  web response).

In this project, when a business rule is broken, the domain *throws* a `DomainException`. That's the
`SalesOrder` refusing to do something illegal — exactly what we want.

---

## 10. Some symbols you'll keep seeing

| Symbol | Name | Read it as |
|---|---|---|
| `=>` | "fat arrow" | "produces" or "goes to." `x => x.Name` = "from x, take Name." |
| `?` after a type | nullable | "might be nothing." `SalesOrder?` = "a SalesOrder, or null (nothing)." |
| `??` | null-coalescing | "or else." `a ?? b` = "a, but if a is nothing, use b." |
| `_lines` | leading underscore | a convention meaning "private field, internal to this class." |
| `//` | comment | a note for humans; the computer ignores it. Read these first! |
| `public` / `private` | access | `public` = anyone can use it; `private` = only this class can. |
| `var` | inferred type | "figure out the type for me." `var x = 5;` → x is an int. |

---

## 11. What is SQL? What is a database?

- A **database** stores your data permanently (so it survives when the program stops). We use **SQL
  Server**.
- Data lives in **tables** — like spreadsheets. A `SalesOrder` table has columns (SalesOrderId,
  CustomerId, Status…) and rows (one per order).
- **SQL** is the language for talking to the database: "give me all orders where status = submitted,"
  "insert this new row."
- A **stored procedure** ("proc") is a chunk of SQL saved *inside* the database with a name, so your C#
  can just call it by name instead of sending raw SQL every time. (Much more in
  [Phase 3](03-database-schema.md).)
- **Dapper** is a small helper that runs our SQL and turns the rows it gets back into C# objects.

Don't worry about writing SQL yet — the phases show you the exact procs. For now: **database = permanent
storage in tables; SQL = the language to talk to it; stored proc = a saved, named SQL command.**

---

## 12. A word about feeling lost

If you read a phase and feel like it went over your head — **that is completely normal and it is not a
sign you can't do this.** Here's what actually works:

1. Read it once for the *big idea*, ignoring details.
2. Work the **Check Yourself** questions at the bottom of the phase — trying to answer them shows you
   exactly which part you didn't actually absorb.
3. Read it a *second* time. It'll be dramatically easier.
4. If a specific word blocks you, look it up in [GLOSSARY.md](GLOSSARY.md) or here.

Nobody learns this in one pass. Seniors just had more passes than you. That's the whole difference.

Now go back to [00-START-HERE.md](00-START-HERE.md) for the plan, then start building in
[01-solution-setup.md](01-solution-setup.md). You've got this.
