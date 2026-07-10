# Glossary — Every Term in Plain English

Keep this open while you read the phases. Terms are grouped by topic, not alphabetized, so related ideas sit
together. Each has a one-line "official-ish" meaning **and** an everyday analogy.

---

## The big architecture words

**Domain**
The part of your code that is about the *business* — orders, customers, money, rules — and nothing about
databases or websites. *Analogy: the actual recipe, separate from the oven and the plates.*

**Business rule**
A statement about what the business allows. "Only draft orders can be submitted." "Orders over ₱50,000 are
flagged as high-value." *Analogy: the rules of a board game.*

**Invariant**
A rule that must *always* be true about an object, no matter what. "An order's total always equals the sum of
its lines." *Analogy: "you can't have negative money in Monopoly."*

**Anemic domain (a bad thing)**
A `SalesOrder` object that's just a bag of data with no rules — anyone can set anything. The rules end up
scattered in other files and drift apart. *Analogy: a vending machine that hands out snacks even when you
didn't pay, because the "did they pay?" check lives in a different machine across the room.*

**Rich domain (the goal)**
The opposite: the `SalesOrder` object *itself* enforces its rules. You literally cannot put it in an illegal
state. *Analogy: a good vending machine — it won't dispense until you've paid.*

---

## The modular monolith structure

**Monolith**
One deployable program (one thing you run and ship), as opposed to many separate networked services.

**Modular monolith**
One deployable program, but internally split into self-contained **modules** with clear walls, each *able* to
become its own program later. *Analogy: one office building where each department has its own locked floor.*

**Module**
A self-contained slice of the app for one business capability — `Sales`, `Inventory` — with its own domain,
features, and database schema. One module never reaches into another's internals. *Analogy: a department that
keeps its own files and won't let other departments rummage through them.*

**Shared kernel** (`src/Shared/...`)
The small set of building blocks the modules lean on: base domain types (`BuildingBlocks`), common helpers and
**interfaces** (`Common`), and the mediator (`Messaging`). A fourth project, `Infrastructure`, holds the
*implementations* of those interfaces (SQL Server, JWT, `HttpContext`) — and **no module references it**; only
the host does. *Analogy: the building's shared utilities. Every floor uses the power sockets (`Common`); nobody
but the building manager goes down to the generator room (`Infrastructure`).*

**Vertical slice (feature)**
Everything for one task ("create a sales order") kept together in one small folder — as two files: the handler
file (Command, Result, Validator, Handler) and the endpoint file (Request, Response, endpoint). *Analogy: the
"taco drawer" — everything for tacos in one drawer, not spread across five.*

**Contract (published interface)**
The *only* sanctioned way one module talks to another synchronously: a small interface in the shared kernel
that one module implements and another calls (e.g. `IStockReservation`). *Analogy: the request slip you send
through another department's front desk — you never walk into their filing room.*

**Integration event**
A published announcement one module makes that other modules can subscribe to ("SalesOrderCancelled"). Used
for "react afterwards," best-effort reactions across the wall. *Analogy: a company-wide memo — anyone who
cares can act on it.*

**The two rules that replace "Clean Architecture layers"**
(1) *Modules are islands* — a module never references another's internals (compiler-enforced by having no
project reference). (2) *Keep the Domain pure* — inside a module, `Features/` and `Infrastructure/` depend on
`Domain/`, but `Domain/` depends only on shared building blocks, so business rules stay testable in
milliseconds. *Analogy: clean interior walls (islands) + a recipe that never names your oven brand (pure
domain).*

**Composition root**
The one place (`Erp.Api/Program.cs`) where every module gets wired together at startup
(`AddSalesModule()` + `AddInventoryModule()` + `MapCarter()`). *Analogy: the breaker panel where all wiring
meets.*

---

## Commands, queries, handlers

**Command**
A request that *changes* something. "Create this order." "Submit this order." *Analogy: an instruction —
"paint the wall blue."*

**Query**
A request that *reads* something and changes nothing. "Show me order #42." *Analogy: a question — "what color
is the wall?"*

**Handler**
The worker that carries out one specific command or query. `CreateSalesOrderHandler` handles the
`CreateSalesOrderCommand`. *Analogy: the specific employee whose whole job is processing new orders.*

**DTO (Data Transfer Object)**
A plain, dumb data shape used to hand information across a boundary (like an API response or a query result).
No rules, no behavior — just fields. *Analogy: a filled-out paper form.*

---

## The web edge

**Minimal API**
ASP.NET Core's lightweight way to declare HTTP endpoints as small functions, without controllers. *Analogy: a
sticky note "when this URL is hit, do this" instead of a whole filing system.*

**Carter**
A thin library over minimal APIs that lets each feature declare its own endpoint (an `ICarterModule`) right
next to its handler, and auto-discovers them all. VSA carried to the web edge. *Analogy: a reception desk on
each department's floor, instead of one giant switchboard in the lobby.*

**FluentValidation**
A small library for writing input-validation rules ("CustomerId must be > 0") in one readable place. *Analogy:
a checklist the mailroom runs before a form is allowed further in.*

---

## The mediator

**Mediator**
A dispatcher. You give it a command; it finds the right handler and runs it. You never know *which* handler or
how it's wired. *Analogy: a post office — you drop a letter, they deliver it.*

**`ISender` / `Send(command)`**
The one thing you call to use the mediator. `sender.Send(myCommand)` = "post office, please handle this."
*Analogy: dropping the letter in the mailbox.*

**Pipeline behavior**
Extra steps that automatically wrap *every* command — like logging or validation — written once instead of in
every handler. *Analogy: every package gets weighed and stamped automatically.*

---

## Domain-Driven Design building blocks

**Entity**
A business object with an identity that lives over time. Two orders with the same details are still *different
orders*. *Analogy: two people named John Smith are still two different people.*

**Value object**
A business object defined purely by its values, immutable. `Money(10)` is `Money(10)` — interchangeable.
*Analogy: any $10 bill is as good as any other.*

**Aggregate**
A cluster of related objects that must stay consistent together and are treated as one unit. A `SalesOrder`
plus its `SalesOrderLine`s form one aggregate. *Analogy: an order form and its line items — saved together.*

**Aggregate root**
The single "front door" to an aggregate. Outside code holds the `SalesOrder`, never grabs a `SalesOrderLine`
directly. The root guards all the rules. *Analogy: you talk to the store manager, not the stock in the back.*

**Domain event**
A note that something business-meaningful happened: "SalesOrderSubmitted." Other code (even another module)
can react later. *Analogy: a "Sold!" bell that rings — anyone listening can react.*

**Domain service**
A rare special class for stateless domain *logic* that spans *multiple* aggregates and belongs to none of
them — and does **no** I/O (no database, no network). Example: a policy that spreads a whole-order discount
across its lines. Used sparingly. *Analogy: a translator you hire only when two departments need to talk.*
*(Note: something that merely **looks up data** — a class that reads item prices out of a table, say — is a
**read port / data access**, not a domain service. The I/O is the tell.)*

**Repository**
The object that saves and loads *one kind* of aggregate. `ISalesOrderRepository` loads and saves
`SalesOrder`s. *Analogy: the filing clerk for one specific cabinet.*

**Strongly-typed ID**
A distinct type per thing — `SalesOrderId`, `CustomerId`, `ItemId` — instead of a plain `long`. The
compiler stops you passing a customer's id where an order's id is expected. *Analogy: color-coded keys — the
red key won't fit the blue lock.*

---

## Database words

**SQL Server**
The database program that stores our data in tables.

**Schema (database)**
A named grouping of tables/procs inside one database. Each module owns its own (`sales`, `inventory`) — the
database-level echo of the module walls. *Analogy: each department's own labeled filing cabinet.*

**Stored procedure ("proc")**
A named, saved SQL command living inside the database. Our C# calls it by name. *Analogy: a saved recipe the
kitchen already knows.*

**Dapper**
A tiny library that runs our SQL and maps results into C# objects — without hiding the SQL. *Analogy: a
helpful assistant who runs to the filing cabinet with the exact instructions you wrote.*

**ORM (Object-Relational Mapper), e.g. Entity Framework**
A bigger library that auto-generates SQL and tracks changes. Powerful, but hides the database. We're **not**
using one — on purpose. *Analogy: a robot chef that cooks from vague descriptions; you stop learning to cook.*

**Transaction**
A group of database changes that either *all* succeed or *all* get undone. *Analogy: transferring money — both
the withdrawal and the deposit happen, or neither.*

**Concurrency**
What happens when two people act at the same moment. Handling it wrong sells the same last item twice.
*Analogy: two people grabbing for the last sale item.*

**Race condition**
A concurrency bug where the result depends on who happened to go first, and both can "win" incorrectly.
*Analogy: both shoppers grab the last item because neither noticed the other.*

**Atomic**
An operation that happens as one indivisible step — nothing sneaks in halfway. The database can make "check
stock AND reserve it" atomic. *Analogy: a turnstile that lets exactly one person through at a time.*

**Optimistic concurrency / `rowversion`**
A safety check: "only save my change if nobody else changed this since I loaded it." *Analogy: "I'll sign this
contract only if it's still the exact copy I read."*

---

## CQRS

**CQRS**
"Command Query Responsibility Segregation" — handle *changes* and *reads* differently, with different code
paths. *Analogy: the bank teller (careful) vs. the ATM screen (fast).*

**CQRS-lite (what we build)**
Separate command and query *code*, but the *same one database*. We do **not** build separate databases, event
sourcing, or eventual consistency (the heavy stuff full CQRS adds).

---

## Security words

**Authentication** vs **authorization**
Authentication answers *who are you?* (login). Authorization answers *are you allowed to do this?* (roles).
Two different questions, in that order. *Analogy: the bouncer checks your ID, then checks whether your ticket
covers the VIP room.*

**JWT (JSON Web Token)**
A short, signed string the server hands you at login and you send back on every later request. It carries your
identity and role *inside it*, and the signature means the server can trust it without looking anything up.
*Analogy: a festival wristband — the gate staff read it and wave you through; they don't phone the box office.*

**Claim**
One fact inside a JWT: `sub` (which user), `name`, `role`. *Analogy: the printed line on the wristband saying
"VIP."*

**Access token**
The short-lived JWT (15 minutes here) you attach to every request. Short on purpose: a stolen one expires fast.

**Refresh token**
A long-lived, single-use secret you trade in for a fresh access token when the old one expires — so the user
isn't forced to log in every 15 minutes. It's stored in the database (hashed), unlike the access token.

**Rotation and reuse detection**
Every refresh **rotates**: you hand in your token and get a brand-new one, and the old one is dead. If a dead
token is ever presented again, that means someone copied it — so we revoke the whole chain of tokens descended
from it. *Analogy: a cloakroom ticket you swap for a new one each visit; if two people turn up with the same
old ticket, something's wrong and the coat gets held.*

**`SecurityStamp`**
A random value stored on the user row and baked into every refresh token. Changing the password changes the
stamp, which instantly invalidates every outstanding refresh token on every device. *Analogy: rekeying the
lock — every old key stops working at once.*

**API key (`X-Api-Key`)**
A secret identifying the **calling application** (the web app, the mobile app) rather than the user. It gates
even anonymous endpoints like `/auth/login`, which a JWT cannot. *Analogy: the wristband says who you are; the
key on the delivery door says which company's van you drove up in.*

**Password hash / salt**
We never store passwords. We store the output of a slow, one-way function (PBKDF2) fed the password plus a
random per-user **salt**, so identical passwords hash differently and guessing is expensive. *Analogy: you
can't un-bake a cake, and every cake got a different pinch of salt.*

**`ICurrentUser`**
The interface a handler uses to ask "who is calling?" without touching `HttpContext`. It's how data-dependent
authorization ("only your own customers") stays testable. *Analogy: asking the front desk who's in the lobby,
instead of walking out to look.*

---

## Testing words

**Walking skeleton**
The thinnest version of the app that runs end-to-end (web request → response) but does almost nothing. Built
first to prove the plumbing. *Analogy: framing a house and running one lightbulb before decorating.*

**Unit test**
Tests one small piece (usually a domain rule) with no database, no website. Super fast. *Analogy:
taste-testing the sauce alone.*

**Integration test**
Tests several real pieces together, including a real database. Slower. *Analogy: cooking the actual dish and
tasting it.*

**End-to-end (e2e) test**
Tests a whole workflow through the real API, start to finish. Slowest, fewest. *Analogy: serving a full meal
to a guest.*

**Fixture**
Setup code that prepares the world for a test (e.g. creates a fresh test database). *Analogy: setting the
table before the meal.*
