# Phase 0 — Getting Your Head Right (No Code Yet)

> 📄 Expert version: [`../plan-Opus/01-phase-0-orientation.md`](../plan-Opus/01-phase-0-orientation.md)
> · Deeper "why" for WebForms migrants: [`../plan-business-tutorial/`](../plan-business-tutorial/)

**In one sentence:** Before we write any code, we need to change *how you think about where code goes*.
This phase is pure reading — but skipping it makes every later phase confusing.

**What you'll build:** Nothing. This is a mindset lesson.

---

## 1. The old way: everything in one file (WebForms)

If you came from older web frameworks (like ASP.NET WebForms), you're used to this: each web page has a
"code-behind" file, and when a button is clicked, **one method does everything**:

```
OrderEntry.aspx        <- the visible page (buttons, text boxes)
OrderEntry.aspx.vb     <- the code-behind: reads the boxes, checks the input,
                          applies business rules, builds SQL, opens the database,
                          runs it in a transaction, updates the screen — ALL here
```

That one `Button_Click` method: reads the form → checks it → builds a SQL query → talks to the database →
shows the result. Everything for that screen, in one place.

Let's make it concrete. Here's roughly what that one method looks like (simplified, but honest):

```vb
' OrderEntry.aspx.vb  — the code-behind for the "new order" screen
Protected Sub SaveButton_Click(sender As Object, e As EventArgs)
    ' 1) Read the screen controls
    Dim customerId As Integer = Integer.Parse(CustomerIdBox.Text)
    Dim qty As Integer = Integer.Parse(QuantityBox.Text)

    ' 2) Validate the input
    If qty <= 0 Then
        ErrorLabel.Text = "Quantity must be positive"
        Return
    End If

    ' 3) Apply business rules
    Dim customer = LoadCustomer(customerId)
    If customer.IsOnCreditHold Then
        ErrorLabel.Text = "Customer is on credit hold"   ' ← a business rule, living in the screen
        Return
    End If

    ' 4) Build SQL, open a connection, run it in a transaction
    Using conn As New SqlConnection(connectionString)
        conn.Open()
        Using tx = conn.BeginTransaction()
            Dim cmd As New SqlCommand("INSERT INTO SalesOrder ...", conn, tx)
            cmd.ExecuteNonQuery()
            tx.Commit()
        End Using
    End Using

    ' 5) Update the screen
    StatusLabel.Text = "Order saved!"
End Sub
```

Look at everything crammed into that one method: **reading UI controls**, **input checking**, a
**business rule** (`IsOnCreditHold`), **raw SQL**, a **database connection**, a **transaction**, and
**updating the screen**. Seven completely different responsibilities, tangled in a method that only runs
when a specific button on a specific page is clicked.

**There's one genuinely good thing here we want to keep:** *related code lives together.* That's called
**cohesion**, and it's nice — when something about the order screen breaks, you know which file to open.

> ❓ **You might be wondering: "That doesn't look so bad — it's all right there, easy to read."**
> For *one* screen, sure. The pain shows up at *scale*. Imagine 60 screens, each with its own copy of the
> credit-hold check, the transaction code, the error handling. Now a rule changes. You have to find and
> fix it in 60 places, and if you miss 3, those 3 screens now behave *differently* — and no error tells
> you. That silent inconsistency is the enemy. The whole architecture in this project exists to make that
> class of problem *impossible*, not just discouraged.

**But here's what goes wrong:**

- **The screen and the business rules are glued together.** You can't answer "can this order be
  submitted?" without launching a whole website and database. That makes testing slow and painful.
- **Rules get copy-pasted and drift.** "Customer must be active" gets pasted into every page that touches
  customers. Later someone fixes it in one page but not the other five. Now they disagree, and nobody
  knows which is right.
- **There are no walls.** Any page can do anything to any table. Change one table and you have to hunt
  through dozens of files hoping you found them all.

## 2. The new way: keep the good part, drop the bad part

**Vertical Slice Architecture (VSA)** keeps the *cohesion* (related code together) but throws away the
*glued-to-the-screen* problem. Instead of "everything for a **screen** in one file," it's "everything for
a **task** in one small folder":

```
Modules/Sales/Features/CreateSalesOrder/
    CreateSalesOrderHandler.cs    <- Command (the inputs) + Result + Validator + Handler (the steps)
    CreateSalesOrderEndpoint.cs   <- Request + Response + the web entry point
```

One **task** ("create a sales order"), top to bottom, in **two files**. When a bug report says "creating
an order is broken," you open **one folder** — not five scattered layers.

> 🔑 **Why two files and not four (or one)?** We *don't* give every class its own file — that scatters a
> feature you want to read in one sitting. We *don't* cram everything into one giant file either. The
> natural seam is **"the work" vs. "the web edge"**:
> - **`CreateSalesOrderHandler.cs`** holds the *behavior*: the `Command` (what info we need), its `Result`,
>   the `Validator` (is the request well-formed?), and the `Handler` (the steps to do it). You can read
>   and unit-test this file with no web server at all.
> - **`CreateSalesOrderEndpoint.cs`** holds the *web edge*: the `Request` shape (when it differs from the
>   command), the `Response` shape, and the endpoint that receives the HTTP call and hands it inward.
>
> Open two files, understand the whole feature. That's the goal.

Compare that to the WebForms `Button_Click`: the *same work* happens, but now each responsibility has its
own named home, and none of it is tied to a screen.

> 💡 **The kitchen analogy:** VSA organizes by *meal* (a "create order" drawer with everything for that
> job) instead of by *tool type* (all the "handlers" in one drawer, all the "validators" in another). When
> you want tacos, you open the taco drawer — not five different drawers.

> ❓ **You might be wondering: "Isn't this just more files to manage?"** We deliberately keep it to *two*
> small files per feature, so the answer is "barely." And the trade is worth it: each small file is easy
> to understand on its own and easy to test in isolation. The amount you have to hold in your head at once
> goes *down*. That's the number that matters.

## 3. The two rules that keep a modular monolith clean

Some things are shared by *many* features — the `SalesOrder` object itself, the database connection, the
mediator. Those don't go in a single feature's folder. Where they go is governed by **two rules** that
together replace the old "Clean Architecture" four-layer diagram.

### Rule 1 — Modules are islands

The app is split by **department** into **modules**: `Sales`, later `Inventory`. Each module is its own
project — its own island.

```
Sales   ──uses──►  Shared   ◄──uses──  Inventory
  │                                        │
  └────────────  NOT ALLOWED  ─────────────┘
     (Sales may not `using` Inventory's internals, or vice versa)
```

A module may use the **Shared** building blocks, but **one module may never reach into another module's
guts.** If `Sales` needs stock reserved, it does *not* call into Inventory's tables directly — it goes
through a small, published *contract* (you'll build one in Phase 5). Why so strict? Because those walls
are exactly what let you someday lift the whole `Inventory` module out into its own separate program
without untangling it from Sales. No walls, no cheap "split later."

> 💡 **Analogy:** One office building (the monolith), but each department has its own locked floor. HR
> can't wander into Accounting's filing cabinets; they send a request through the front desk. Still one
> building — clear walls inside.

### Rule 2 — Inside a module, keep the Domain pure

Within a module, code is organized into folders, and the dependency arrow points **at the Domain**:

```
   Features/  ──►  Domain/  ◄──  Infrastructure/
   (the tasks)   (business rules —   (database, plumbing)
                  depends on nothing
                  but Shared building blocks)
```

The **Domain** (your `SalesOrder`, your rules) knows *nothing* about SQL, the web, Carter, or Dapper.
That's exactly what lets you test it in milliseconds — there's nothing heavy to start up.

Let me make "depends on nothing" concrete. Say you want to test the rule *"an order over $50,000 needs
approval."* Because `SalesOrder` doesn't touch a database or a website, your test is just:

```csharp
var order = SalesOrder.Create(customerId);
order.AddLine(productId, quantity: 1, unitPrice: new Money(60_000));
// No database. No web server. No setup. Just an object in memory.
Assert.Throws<DomainException>(() => order.Submit(approvalGranted: false));
```

That test runs in **under a millisecond**, on any machine, with nothing to install. If that rule lived in
the WebForms `Button_Click`, you'd need a web server, a page, controls, and a database just to test it.
That's the difference "keep the Domain pure" makes. It's not purity for its own sake; it's *fast,
reliable tests* and *rules you can trust*.

> 🔑 **This is the one genuinely good idea we kept from Clean Architecture** — an isolated, pure domain.
> The difference: we get it with a `Domain/` **folder** inside the module and a little discipline, instead
> of a whole separate `Erp.Domain` **project** and a four-layer ceremony. Same payoff, far less scaffolding.

> ❓ **You might be wondering: "How can the Domain save an order if it's not allowed to know about the
> database?"** The trick: the Domain *declares an interface* (a promise), like `ISalesOrderRepository` with
> a `Save` method, but the *actual* code that talks to SQL lives in the module's `Infrastructure/` folder.
> The Domain says "somebody will save this; I don't care who or how." At runtime, the real database code
> gets handed in (dependency injection, from [Foundations §4](00a-FOUNDATIONS.md)). So the Domain gets its
> order saved *without ever knowing SQL exists.* Phase 4 shows this in full.

## 4. Where does each old idea move to?

Here's the translation table from "WebForms brain" to "new brain":

| Old WebForms thought | Where it lives now | Why it moved |
|---|---|---|
| The `Button_Click` steps | A **Handler** (in the feature folder) | Coordinating steps isn't a UI job; separating it makes it testable. |
| Checking the input in the page | A **Validator** (same file as the handler) | "Is this form filled in correctly?" is its own concern, checked first. |
| "Can this order be submitted?" | A **method on the `SalesOrder` object** (Domain) | That's a rule about the *thing*, not the screen. It belongs to `SalesOrder`. |
| Inline SQL + connection | A **Repository** (module's `Infrastructure/`) | Database detail, hidden behind a clean door so the rules stay pure. |
| The transaction in code-behind | The **Handler** (kept visible on purpose) | The transaction boundary is a decision we want to *see* while learning. |
| The copy-pasted rule | **One place** (the module's Domain) | One source of truth means it can't drift anymore. |
| "Any page can hit any table" | **Module walls** | Sales can't touch Inventory's tables; it asks through a contract. |

## 5. A worked example: "where does each part go?"

Take this business statement:

> *"An order over $50,000 needs manager approval before it can be submitted."*

Watch how we split it into pieces, each going to a different home:

- *"Is this even a valid request — is the order ID present and well-formed?"*
  → **Validator.**
- *"Is $50,000 the limit, and does this order cross it?"*
  → **Domain.** The `SalesOrder` knows its own total; the $50,000 threshold is a business rule. The
  `SalesOrder.Submit()` method itself refuses if approval is needed but missing.
- *"Load the order, call `Submit()`, save it, commit."*
  → **Handler.** Just coordination.
- *"Save the new status safely, even if two managers click at the same instant."*
  → **Stored procedure** in the database, because safety-under-simultaneous-access is a database strength
  (much more in Phase 5).

Notice: **no single place does all of it**, and *none* of it lives in a "page." Learning to do this split
*is the core skill* this whole project builds. It'll feel unnatural at first and obvious by Phase 7.

### The mental question to ask every time

When deciding where a piece of logic belongs, run it through these in order:

1. **"Is this just checking the request is well-formed?"** (empty fields, negative numbers, missing ids)
   → **Validator.**
2. **"Is this a rule about what the business allows?"** (credit holds, status transitions, approval
   thresholds) → **Domain**, on the object it's a rule *about*.
3. **"Is this just coordinating steps — load, do, save?"** → **Handler.**
4. **"Is this about *how* data is stored, or staying correct when two people act at once?"** →
   **Infrastructure / stored procedure.**
5. **"Does this need something another module owns?"** (e.g. Sales needs stock reserved) → **a contract**,
   never a direct reach across the wall.

You will get these wrong sometimes. That's fine — the *pitfalls* section of each later phase calls out the
exact mistakes ("a business `if` sneaking into the handler"), so you'll learn to catch yourself.

> ❓ **You might be wondering: "This seems like a lot of ceremony for an order form. Isn't it
> over-engineered?"** For a to-do app, yes — and a good engineer would *not* use this there. This project
> uses it because the domain has *real, tricky rules* (approval thresholds, stock reservation under
> concurrency, status state machines) and *multiple departments*. The pattern earns its keep when the
> rules are genuinely hard and the system will grow. Knowing *when this is overkill* is itself part of what
> you're learning — we say so out loud in Phase 2.

---

## ✅ Check Yourself

You understand this phase when you can answer these (try before moving on):

1. **What does the new approach keep from WebForms, and what does it throw away?**
   *(Hint: it keeps related-code-together; it drops glued-to-the-screen.)*
2. **State the two rules of the modular monolith, and why each exists.**
   *(Hint: modules are islands so you can split later; keep the domain pure so rules are testable.)*
3. **For the "$50,000 approval" rule, name which piece goes to the validator, the domain, the handler,
   and the stored procedure — and why each.**
4. **Why is a copy-pasted "customer must be active" check a problem, and where does it live now?**
5. **Why is a feature two files instead of four separate ones — or one big one?**

No code yet. Move to Phase 1 only when those answers feel comfortable.

---

## 👨‍🏫 Answer Key (try the questions first, then check yourself here)

> Don't feel bad if your words differ from mine — you only need the *idea* right, not the exact phrasing.
> If an answer surprises you, re-read the section it came from; that's the part to slow down on.

**1. What does the new approach keep from WebForms, and what does it throw away?**
It **keeps cohesion** — related code living together (WebForms put everything for a *screen* in one file;
we put everything for a *task* in one folder). It **throws away the coupling to the screen** — in WebForms
the business rules were glued to a web page, so you couldn't test or reuse them without a whole website.

**2. State the two rules of the modular monolith, and why each exists.**
- **Rule 1 — Modules are islands:** a module (Sales) may not reach into another module's (Inventory)
  internals; they talk through a published contract. This exists so you can later extract a module into
  its own program cheaply — the walls are already there.
- **Rule 2 — Keep the Domain pure:** inside a module, `Features/` and `Infrastructure/` depend on
  `Domain/`, but `Domain/` depends on nothing but shared building blocks. This exists so business rules
  are testable in milliseconds with no database or website. (This is the one good idea kept from Clean
  Architecture — minus the four-project ceremony.)

**3. For the "$50,000 approval" rule, name which piece goes where — and why.**
- **Validator:** "is the request even well-formed — is the order id present and sensible?" (shape of the
  request, checked before real work).
- **Domain:** "is $50,000 the limit, and does this order cross it?" `SalesOrder` knows its own total, and
  the threshold is a business rule — so `SalesOrder.Submit()` itself refuses when approval is needed but
  missing.
- **Handler:** "load the order, call `Submit()`, save, commit." Pure coordination.
- **Stored procedure:** "save the new status safely even if two managers click at the same instant."
  Safety-under-simultaneous-access is a database strength (Phase 5 goes deep).

**4. Why is a copy-pasted "customer must be active" check a problem, and where does it live now?**
Because copies **drift**: someone changes the rule in one place and forgets the other five, so they
silently disagree. The fix is a **single source of truth**: the rule lives in *one* place in the Domain
(e.g. `Customer.EnsureCanOrder()`), and every feature calls that one method.

**5. Why is a feature two files instead of four separate ones — or one big one?**
Because the natural seam is **behavior vs. web edge**. The `Handler.cs` file (Command, Result, Validator,
Handler) is the behavior — readable and testable with no web server. The `Endpoint.cs` file (Request,
Response, endpoint) is the web edge. Two files means you understand the whole feature by opening one
folder; four files scatter it; one file buries the seam.

> ✅ **You're ready for Phase 1 if:** you can explain "cohesion good, screen-coupling bad," state the two
> modular-monolith rules and *why*, and split that $50,000 rule across the four homes. If any is fuzzy,
> re-read the matching section — this mental model is the foundation for everything after.
