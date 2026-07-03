# Glossary — Every Term in Plain English

Keep this open while you read the phases. Terms are grouped by topic, not alphabetized, so related
ideas sit together. Each has a one-line "official-ish" meaning **and** an everyday analogy.

---

## The big architecture words

**Domain**
The part of your code that is about the *business* — orders, customers, money, rules — and nothing
about databases or websites. *Analogy: the actual recipe, separate from the oven and the plates.*

**Business rule**
A statement about what the business allows. "Only draft orders can be booked." "Orders over $50,000
need approval." *Analogy: the rules of a board game.*

**Invariant**
A rule that must *always* be true about an object, no matter what. "An order's total always equals the
sum of its lines." *Analogy: a rule that can never be broken while the game is running — like "you
can't have negative money in Monopoly."*

**Anemic domain (a bad thing)**
An `Order` object that's just a bag of data with no rules — anyone can set anything to anything. The
rules end up scattered in other files and drift apart. *Analogy: a vending machine that hands out
snacks even when you didn't pay, because the "did they pay?" check lives in a totally different
machine across the room.*

**Rich domain (the goal)**
The opposite: the `Order` object *itself* enforces its rules. You literally cannot put it in an
illegal state. *Analogy: a good vending machine — it won't dispense until you've paid.*

---

## The four project layers

**Layer**
A big slice of the app with a specific job. We have four. The golden rule: inner layers don't know
about outer layers.

**Domain layer** (`Erp.Domain`)
The business objects and rules. Depends on *nothing* else. *Analogy: the recipe.*

**Application layer** (`Erp.Application`)
The "orchestrators" — code that coordinates a single task ("to create an order: load the customer,
build the order, save it"). *Analogy: the chef following the recipe, fetching ingredients in order.*

**Infrastructure layer** (`Erp.Infrastructure`)
The technical plumbing: talking to the database, the mediator's guts. *Analogy: the oven, fridge, and
plumbing.*

**API layer** (`Erp.Api`)
The thin outer shell that receives web requests and passes them inward. *Analogy: the waiter who takes
your order and brings it to the kitchen.*

**The dependency rule**
Code is only allowed to depend "inward." The API can use the Application; the Application can use the
Domain; the Domain uses nobody. This keeps the important business code clean and testable. *Analogy:
the recipe never mentions your specific oven brand.*

---

## Commands, queries, handlers

**Command**
A request that *changes* something. "Create this order." "Book this order." *Analogy: an instruction —
"paint the wall blue."*

**Query**
A request that *reads* something and changes nothing. "Show me order #42." *Analogy: a question — "what
color is the wall?"*

**Handler**
The worker that carries out one specific command or query. `CreateOrderHandler` handles the
`CreateOrderCommand`. *Analogy: the specific employee whose whole job is processing new orders.*

**DTO (Data Transfer Object)**
A plain, dumb data shape used to hand information across a boundary (like an API response). No rules,
no behavior — just fields. *Analogy: a filled-out paper form.*

---

## The mediator

**Mediator**
A dispatcher. You give it a command; it finds the right handler and runs it. You never have to know
*which* handler or how it's wired. *Analogy: a post office — you drop a letter, they deliver it; you
don't need to know the mail carrier's name.*

**`ISender` / `Send(command)`**
The one thing you call to use the mediator. `sender.Send(myCommand)` = "post office, please handle
this." *Analogy: dropping the letter in the mailbox.*

**Pipeline behavior**
Extra steps that automatically wrap *every* command — like logging, or validation — so you write them
once instead of in every handler. *Analogy: every package that goes through the post office
automatically gets weighed and stamped; you don't do it per-letter.*

---

## Domain-Driven Design building blocks

**Entity**
A business object with an identity that lives over time. Two orders with the same details are still
*different orders* because they have different IDs. *Analogy: two people named John Smith are still two
different people.*

**Value object**
A business object defined purely by its values, with no identity. Immutable (never changes after
creation). `Money(10)` is `Money(10)` — interchangeable. *Analogy: a $10 bill — any $10 bill is as good
as any other; you don't track *which* one.*

**Aggregate**
A cluster of related objects that must stay consistent together and are treated as one unit. An
`Order` plus its `OrderLine`s form one aggregate. *Analogy: an order form and its line items — you
save or change them together, never half of one.*

**Aggregate root**
The single "front door" to an aggregate. Outside code holds the `Order`, never reaches inside to grab
an `OrderLine` directly. The root guards all the rules. *Analogy: you talk to the store manager, not
directly to the items on the shelves in the back.*

**Domain event**
A note that something business-meaningful happened: "OrderBooked." Other code can react to it later.
*Analogy: a "Sold!" bell that rings — anyone listening can react (update a report, send an email).*

**Domain service**
A rare special class for logic that spans *multiple* aggregates and has no natural home on any single
object. Used sparingly. *Analogy: a translator you only hire when two departments that don't share a
language need to talk — not for everyday chores.*

**Repository**
The object that saves and loads *one kind* of aggregate to/from the database. `IOrderRepository` loads
and saves `Order`s. *Analogy: the filing clerk for one specific cabinet.*

**Strongly-typed ID**
Instead of a plain `long` for every ID, we make a distinct type per thing: `OrderId`, `CustomerId`.
Now the compiler stops you from accidentally passing a customer's ID where an order's ID was expected.
*Analogy: color-coded keys — you physically can't put the red key in the blue lock.*

---

## Database words

**SQL Server**
The database program that stores our data in tables.

**Stored procedure ("proc")**
A named, saved SQL command living inside the database. Our C# calls it by name. *Analogy: a saved,
named recipe the kitchen (database) already knows how to make.*

**Dapper**
A tiny library that runs our SQL and maps the results into C# objects — without hiding the SQL from us.
*Analogy: a helpful assistant who runs to the filing cabinet with the exact instructions you wrote.*

**ORM (Object-Relational Mapper), e.g. Entity Framework**
A bigger library that auto-generates SQL for you and tracks changes. Powerful, but hides the database.
We're **not** using one — on purpose. *Analogy: a robot chef that cooks from vague descriptions; great
sometimes, but you stop learning to cook.*

**Transaction**
A group of database changes that either *all* succeed or *all* get undone — never half. *Analogy:
transferring money between two accounts: both the withdrawal and the deposit happen, or neither does.*

**Concurrency**
What happens when two people (or requests) act at the same moment. Handling it wrong causes bugs like
selling the same last item twice. *Analogy: two people grabbing for the last sale item at once.*

**Race condition**
A specific concurrency bug where the result depends on who happened to go first, and both can "win"
incorrectly. *Analogy: both shoppers grab the last item because neither noticed the other.*

**Atomic**
An operation that happens as one indivisible step — nothing can sneak in halfway. The database can make
"check stock AND reserve it" atomic so two buyers can't both win. *Analogy: a turnstile that lets
exactly one person through at a time.*

**Optimistic concurrency / `rowversion`**
A safety check: "only save my change if nobody else changed this since I loaded it." The `rowversion`
is a hidden stamp that changes on every update. *Analogy: "I'll sign this contract only if it's still
the exact copy I read — if someone edited it meanwhile, stop."*

---

## CQRS

**CQRS**
"Command Query Responsibility Segregation." A fancy way of saying: handle *changes* and *reads*
differently, with different code paths. *Analogy: the bank teller (careful) vs. the ATM screen
(fast).*

**CQRS-lite (what we build)**
The gentle version: separate command and query *code*, but the *same one database*. We do **not** build
separate databases, event sourcing, or eventual consistency (the heavy stuff full CQRS adds).

---

## Architecture-shape words

**Vertical Slice Architecture (VSA)**
Organize code by *feature* ("everything for CreateOrder in one folder"), not by *technical type* ("all
handlers in one folder, all validators in another"). *Analogy: the "taco drawer" kitchen.*

**Walking skeleton**
The thinnest possible version of the app that runs end-to-end (web request → response) but does almost
nothing. You build it first to prove the plumbing works. *Analogy: framing a house and running one
lightbulb before you decorate any rooms.*

**Modular monolith**
One deployable program, but internally organized into clean modules that *could* be split into separate
programs later. *Analogy: a house with clearly separate, self-contained rooms — you could add a wall
and a second front door later without gutting it.*

**Composition root**
The one place (in `Program.cs`) where all the pieces get wired together at startup. *Analogy: the
breaker panel where all the wiring meets.*

---

## Testing words

**Unit test**
Tests one small piece (usually a domain rule) with no database, no website. Super fast. *Analogy:
taste-testing the sauce alone.*

**Integration test**
Tests several real pieces together, including a real database. Slower. *Analogy: cooking the actual
dish and tasting it.*

**End-to-end (e2e) test**
Tests a whole workflow through the real API, start to finish. Slowest, fewest of them. *Analogy:
serving a full meal to a guest and checking they're happy.*

**Fixture**
Setup code that prepares the world for a test (e.g. creates a fresh test database). *Analogy: setting
the table before the meal.*
