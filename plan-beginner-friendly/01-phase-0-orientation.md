# Phase 0 — Getting Your Head Right (No Code Yet)

> 📄 Expert version: [`../plan-Opus/01-phase-0-orientation.md`](../plan-Opus/01-phase-0-orientation.md)

**In one sentence:** Before we write any code, we need to change *how you think about where code
goes*. This phase is pure reading — but skipping it makes every later phase confusing.

**What you'll build:** Nothing. This is a mindset lesson.

---

## 1. The old way: everything in one file (WebForms)

If you came from older web frameworks (like ASP.NET WebForms), you're used to this: each web page has
a "code-behind" file, and when a button is clicked, **one method does everything**:

```
OrderEntry.aspx        <- the visible page (buttons, text boxes)
OrderEntry.aspx.vb     <- the code-behind: reads the boxes, checks the input,
                          applies business rules, builds SQL, opens the database,
                          runs it in a transaction, updates the screen — ALL here
```

That one `Button_Click` method: reads the form → checks it → builds a SQL query → talks to the
database → shows the result. Everything for that screen, in one place.

**There's one genuinely good thing here we want to keep:** *related code lives together.* That's called
**cohesion**, and it's nice — when something about the order screen breaks, you know which file to open.

**But here's what goes wrong:**

- **The screen and the business rules are glued together.** You can't answer "can this order be
  booked?" without launching a whole website and database. That makes testing slow and painful.
- **Rules get copy-pasted and drift.** The check "customer must be active" gets pasted into every page
  that touches customers. Later someone fixes it in one page but not the other five. Now they disagree,
  and nobody knows which is right.
- **There are no walls.** Any page can do anything to any table. Change one table and you have to hunt
  through dozens of files hoping you found them all.

## 2. The new way: keep the good part, drop the bad part

**Vertical Slice Architecture (VSA)** keeps the *cohesion* (related code together) but throws away the
*glued-to-the-screen* problem. Instead of "everything for a **screen** in one file," it's "everything
for a **task** in one folder":

```
Features/Orders/CreateOrder/
    CreateOrderCommand.cs      <- the request: what info do we need?
    CreateOrderValidator.cs    <- is the input well-formed?
    CreateOrderHandler.cs      <- the steps to actually do it
    CreateOrderEndpoint.cs     <- the web entry point
```

One **task** ("create an order"), top to bottom, in one folder. When a bug report says "creating an
order is broken," you open **one folder** — not five scattered layers.

> 💡 **The kitchen analogy again:** VSA organizes by *meal* (a "create order" drawer with everything
> for that job) instead of by *tool type* (all the "handlers" in one drawer, all the "validators" in
> another). When you want to make tacos, you open the taco drawer — not five different drawers.

## 3. The one rule Clean Architecture adds: the arrow only points inward

Some things are shared by *many* tasks — like the `Order` object itself, or the database connection.
Those don't go in a single task's folder; they live in shared layers. **Clean Architecture** governs
how those layers may depend on each other, with a single simple rule:

```
   Api  ──►  Application  ──►  Domain
              (tasks)      (business rules — depends on NOTHING)
                 │              ▲
                 └──► Infrastructure ─┘
                       (database, plumbing)
```

Read the arrows as "is allowed to use." The **Domain** (your `Order`, your rules) sits at the center
and knows *nothing* about SQL, the web, or Dapper. That's exactly what lets you test it in
milliseconds — there's nothing heavy to start up.

> 💡 **Analogy:** A recipe (Domain) doesn't mention your specific oven brand (Infrastructure). That's
> why the same recipe works in any kitchen. If your recipe suddenly required a *specific* oven, it'd be
> useless to everyone else — and that's the mess we're avoiding.

## 4. Where does each old idea move to?

Here's the translation table from "WebForms brain" to "new brain":

| Old WebForms thought | Where it lives now | Why it moved |
|---|---|---|
| The `Button_Click` steps | A **Handler** | Coordinating steps isn't a UI job; separating it makes it testable. |
| Checking the input in the page | A **Validator** | "Is this form filled in correctly?" is its own concern, checked first. |
| "Can this order be booked?" | A **method on the `Order` object** | That's a rule about the *thing*, not the screen. It belongs to `Order`. |
| Inline SQL + connection | A **Repository** (in Infrastructure) | Database detail, hidden behind a clean door so the rules stay pure. |
| The transaction in code-behind | The **Handler** (kept visible on purpose) | The transaction boundary is a decision we want to *see* while learning. |
| The copy-pasted rule | **One place** (the Domain) | One source of truth means it can't drift anymore. |

## 5. A worked example: "where does each part go?"

Take this business statement:

> *"An order over $50,000 needs manager approval before it can be booked."*

Watch how we split it into pieces, each going to a different home:

- *"Is this even a valid booking request — is the order ID present and well-formed?"*
  → **Validator.**
- *"Is $50,000 the limit, and does this order cross it?"*
  → **Domain.** The `Order` knows its own total; the $50,000 threshold is a business rule. The
  `Order.Book()` method itself refuses if approval is needed but missing.
- *"Load the order, call `Book()`, save it, commit."*
  → **Handler.** Just coordination.
- *"Save the new status safely, even if two managers click at the same instant."*
  → **Stored procedure** in the database, because safety-under-simultaneous-access is a database
  strength (much more on this in Phase 5).

Notice: **no single place does all of it**, and *none* of it lives in a "page." Learning to do this
split *is the core skill* this whole project builds. It'll feel unnatural at first and obvious by
Phase 7.

---

## ✅ Check Yourself

You understand this phase when you can answer these (try before moving on):

1. **What does the new approach keep from WebForms, and what does it throw away?**
   *(Hint: it keeps related-code-together; it drops glued-to-the-screen.)*
2. **Which way does the dependency arrow point, and why does the Domain depend on nothing?**
   *(Hint: so the business rules can be tested with no database and reused anywhere.)*
3. **For the "$50,000 approval" rule, name which piece goes to the validator, the domain, the handler,
   and the stored procedure — and why each.**
4. **Why is a copy-pasted "customer must be active" check a problem, and where does it live now?**
   *(Hint: copies drift apart; now it lives in one place in the Domain.)*

No code yet. Move to Phase 1 only when those answers feel comfortable.
