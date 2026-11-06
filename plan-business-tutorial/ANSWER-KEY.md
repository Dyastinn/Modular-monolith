# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--mediator) · [03](#lesson-03--domain-model) ·
[04](#lesson-04--database) · [05](#lesson-05--sign-in) · [06](#lesson-06--create-an-order) ·
[07](#lesson-07--find-orders) · [08](#lesson-08--submit) · [09](#lesson-09--approve-reject-cancel) ·
[10](#lesson-10--update-an-order) · [11](#lesson-11--events-and-the-outbox) · [12](#lesson-12--testing) ·
[13](#lesson-13--observability) · [14](#lesson-14--customers) · [15](#lesson-15--users-and-email) ·
[16](#lesson-16--item-catalog-and-stock) · [17](#lesson-17--shipping) · [18](#lesson-18--purchasing) ·
[19](#lesson-19--invoicing) · [20](#lesson-20--payments-and-receivables) ·
[21](#lesson-21--security-hardening) · [22](#lesson-22--docker) · [23](#lesson-23--deploy-to-iis) ·
[24](#lesson-24--production-checklist)

---

## Lesson 01 — Solution setup

**1. Why doesn't `Erp.Modules.Sales` reference `Erp.Shared.Infrastructure`? What could go wrong if it did?**
Infrastructure holds the real implementations: the PostgreSQL connection, the code that reads the user from
the HTTP request. Without the reference, Sales physically can't reach a connection string or `HttpContext`;
it only sees the small interfaces in BuildingBlocks. With it, a handler could grab the raw request or open
its own connection, and every module would be tied to those details.

**2. You add a sixth shared project, and every module references it. What does that new project boundary
enforce?**
Nothing. A project boundary exists to make something a compile error. If every module may reference it, it
forbids nothing, and it should have been a folder in BuildingBlocks.

**3. How many files change when you upgrade a package the whole solution uses?**
One: `Directory.Packages.props`. Every project takes its version from there.

**4. Why vertical slices instead of `Controllers`, `Services` and `Repositories` folders?**
Because a change is almost always about one feature. With slices, everything for "create an order" sits in
one folder, like one drawer holding everything for tacos. With technical folders, one feature is spread over
four places, and shared "services" quietly tie features together.

**5. Why Blazor WebAssembly, and what does it cost on the first visit?**
One language and toolchain for the whole product, C# in the browser talking to our own API, and a server
that only answers API calls instead of holding a live connection per browser. The cost: the first visit
downloads the .NET runtime and the app, a few megabytes, so it takes a second or two to start. After that
it's cached.

**6. Why does the API live under `/api`, and what does `GET /api/typo` answer?**
The app wants page addresses like `/sales-orders` for itself, and the API wants addresses for data; one
prefix keeps them apart. `GET /api/typo` hits the API group's own fallback and answers 404, never the app's
HTML.

## Lesson 02 — Mediator

**1. Why does the generator need `InternalsVisibleTo("Erp.Api")`? Why is granting it to the host safe, but
granting it to another module not?**
The generator runs in the host and writes code that names each module's internal messages and handlers;
without access, that code doesn't compile (`CS0122`). The host already references every module, and wiring
them is its job. Another module seeing those internals would knock down the wall between two islands.

**2. You forget `includeInternalTypes: true`. What happens when you send an empty message?**
The validator is never found, so it never runs. The empty message reaches the handler and comes back as
`pong: `, with no error at all.

**3. Why is `LoggingBehavior` registered before `ValidationBehavior`?**
Behaviors run outermost first. With logging outside, it also records requests that fail validation.

**4. What's the difference between a command and a query, and why tell them apart?**
A command changes something; a query only reads. Telling them apart lets each use the right tool (EF Core and
the domain for commands, Dapper for queries) and makes it obvious which requests are safe to repeat.

**5. Name one real cost of the mediator pattern.**
Indirection: from an endpoint you can't click straight through to the code that runs; you follow the
message type to its handler. Keeping the handler in the same file as its message softens it.

## Lesson 03 — Domain model

**1. Why does `EnsureWithinCreditLimit` take `otherPendingTotal`? Describe the bug without it.**
Orders waiting for a manager aren't in the balance yet. Without them, a customer with ₱100,000 of room could
submit three ₱90,000 orders, each passing on its own, and owe ₱270,000 once all are approved.

**2. Where is "over ₱50,000 needs a manager" enforced, and how does a small order skip the manager?**
In `SalesOrder.Submit`, using `ApprovalPolicy.Threshold`. If the total is at or below it, `Submit` calls
`MarkApproved` with no approver on the spot.

**3. Why does `Reject` return the lines, instead of the handler deciding what to release?**
Because the order knows whether it holds stock. If the handler decided, a business `if` would leak out of the
domain, and every handler would have to get it right.

**4. What stops a handler from setting `order.Status = Approved` on a draft?**
The setter is private, so it doesn't compile. The only way to change status is a domain method, which goes
through the transition table.

**5. Why check the category cap across the whole order?**
Because the rule is "at most 5 per order". Five Controlled items split over two lines would pass a per-line
check.

**6. Why does an order line keep the item's name?**
An order is a record of what was sold. Rename the item next year, and this order must still say what was
ordered, like a printed copy would.

## Lesson 04 — Database

**1. Why does each module get its own DbContext and schema?**
So the wall between modules reaches into the database: a module's context can't even see another module's
tables, so it can't query them. Each module also migrates on its own.

**2. What does `Configure(...)` protect you from, and who calls it?**
The app and the `dotnet ef` tool drifting apart in how the context is configured. Both the module's
registration and the design-time factory call it.

**3. Who calls `Customer`'s private parameterless constructor, and why not `Create`?**
EF Core, when it loads a saved customer. Loading history isn't a new business event, so the creation rules
must not run again.

**4. "10.0.4 or newer" resolves to which version, and why pin EF Core?**
The lowest that fits: 10.0.4. The design package pulls 10.0.14, so without the pin the app would mix two EF
Core versions. Transitive pinning makes 10.0.14 win everywhere.

**5. Why does the seed use `WHERE NOT EXISTS`?**
It runs on every development start; the second run must insert nothing.

**6. Why no foreign key from `sales_order.created_by` to a users table?**
Users live in another module's schema. A foreign key across schemas would weld the modules together. Store
the id; show the name through the other module's API.

## Lesson 05 — Sign-in

**1. Three things production sign-in needs that a hand-written version forgets.**
Any three of: safe password hashing with a work factor, lockout after repeated failures, expiring tokens with
refresh, revoking every session, cookies that scripts can't read, constant-time comparisons, a security stamp
that invalidates old sessions.

**2. Why block `/auth/register`, and how?**
Anyone could create an account in the company's ERP. `MapIdentityApi` always maps it, so an endpoint filter
answers 404 for any path ending in `/auth/register`.

**3. A browser and a mobile app: how does each stay signed in?**
The browser with an HttpOnly cookie it sends automatically; the mobile app with a bearer token in the
`Authorization` header, renewed with a refresh token.

**4. Why 12 characters with no "must contain a digit"?**
Length beats composition (NIST SP 800-63B). Composition rules push people toward predictable passwords like
`Password1!`.

**5. What does "sign out everywhere" change, and why does it end every session?**
It updates the user's security stamp. Every cookie and token carries the old stamp, so each fails its next
check.

**6. Why `CurrentUser` in Infrastructure and `ICurrentUser` in BuildingBlocks?**
Modules code against the small interface. Only Infrastructure, which only the host sees, may touch
`HttpContext`.

**7. The browser app can't read the cookie. How does it know who's signed in?**
It asks `GET /api/auth/me`. A 200 says who; a 401 means signed out. `CookieAuthenticationStateProvider` turns
the answer into claims for `<AuthorizeView>`.

**8. What is an open redirect, and which line prevents it?**
A sign-in page that sends you to any `returnUrl` after signing in, including an attacker's look-alike site.
The check that only follows paths without `//` or `:` keeps it inside the app.

## Lesson 06 — Create an order

**1. Validator or domain: "quantity is positive", "customer isn't blocked", "at most 5 Controlled"?**
Quantity positive: validator (shape). Blocked and the category cap: domain (business rules about this
customer and this order).

**2. Why does `MAX(order_number) + 1` go wrong, and how does a sequence avoid it?**
Two saves at once both read 41 and both write 42. `nextval()` hands every caller a different number,
atomically.

**3. Why both a request and a command?**
The request is the HTTP contract; the command is the internal message. Today they match; tomorrow they can
differ, a renamed field or one the command shouldn't trust, without either side breaking.

**4. Blocked customer, empty order, unknown item: which status each?**
Blocked: 422 (a business rule said no). Empty: 400 (the validator, malformed). Unknown item: 404 (something
it refers to doesn't exist).

**5. Why a `traceId` in the error body?**
A user can quote it, and you can find that exact request's logs and trace.

**6. Why Swagger only in Development?**
A production server shouldn't advertise its API to strangers.

## Lesson 07 — Find orders

**1. Why don't query handlers load aggregates with EF Core?**
A screen wants a shape, not an aggregate. Loading and tracking full orders with every line to show six
columns is wasted work; SQL builds the shape directly.

**2. Why functions rather than stored procedures?**
In PostgreSQL, a procedure can't return rows; a function `RETURNS TABLE` can.

**3. A function needs a new column. What do you add, and what must you never do?**
A `.v2.sql` file (dropping v1 first if the result columns change) and a new migration that applies it. Never
edit a released `.sql` file: the old migration still reads it.

**4. A rep asks for a manager's order. What's the answer, and why?**
404. A 403 would admit the order exists; a 404 reveals nothing.

**5. Where is "a rep sees only their own" decided, and how do both reads use it?**
In the domain, `SalesOrder.CreatorFilterFor`. The handlers pass its answer to the SQL functions as
`p_created_by`, so the list and the single read filter the same way. Commands use `IsVisibleTo`, built on it.

**6. `page` declared as `int` instead of `int?`?**
Minimal APIs treat it as required, so a bare `GET /api/sales-orders` fails with 400 before the handler runs.

**7. Why does the browser app keep its own copies of the API's shapes?**
The server's DTOs are internal to each module, and the app must not reference a module. The HTTP contract
is the only thing they share.

## Lesson 08 — Submit

**1. Why reserve at submit, not at approval?**
So a manager never approves an order that can't be filled. "Pending approval" means "we're holding this".

**2. What goes wrong with read, check in C#, then save? What fixes it?**
Two requests both read "8 available" and both reserve 5. One `UPDATE ... WHERE on_hand - reserved >= 5` makes
the check and the write one atomic step.

**3. Why reference Inventory's Contracts, not Inventory?**
Contracts is the island's service window. It references no module, so modules can call each other both ways
without a cycle, and Sales can't touch Inventory's internals.

**4. Why one shared connection per request?**
A transaction lives on one connection. For Inventory to join Sales' transaction, both contexts must use the
same connection.

**5. The third line is out of stock: what happens to the first two reservations?**
They roll back with the transaction. The handler throws, the transaction isn't committed, and every change in
it, Inventory's included, is undone.

**6. Two submits for one customer at once: what does `FOR UPDATE` do?**
The second waits at the lock until the first commits, then sees the first order as pending and checks credit
against true numbers.

## Lesson 09 — Approve, reject, cancel

**1. A lost update with two approvals.**
Both managers load a customer owing ₱0, each approves a ₱60,000 order, each saves "owes ₱60,000". The second
overwrites the first, and ₱60,000 of debt vanishes without an error.

**2. What does EF add once `xmin` is a row version, and what if no row matches?**
`AND xmin = <loaded version>` in the `WHERE`. If no row matches, EF throws `DbUpdateConcurrencyException`,
which becomes 409.

**3. Why does approve need no explicit transaction while cancel does?**
Approve changes only Sales' own rows, and one `SaveChanges` is one transaction. Cancel also releases stock in
Inventory, which must join the same transaction.

**4. "Only managers approve" and "not your own order": where, and why?**
The first at the endpoint (`RequireRole`): it doesn't depend on data. The second in the domain: it depends on
who created this order.

**5. Why is the reason checked twice?**
The validator checks its shape (present, ≤500 characters) for a clear 400; the domain enforces the rule (a
real reason, not blanks) for every caller.

**6. When would you lock up front instead?**
When clashes are likely, or when a check spans many rows, like the credit check across all pending orders.

## Lesson 10 — Update an order

**1. Why replace the whole list?**
Simpler, and nothing can drift: the client sends exactly what the order should look like afterwards.

**2. Why doesn't `xmin` catch Ana and Ben without the override?**
A lines-only edit deletes and inserts line rows but never updates the order row, and only updated rows have
their version checked.

**3. What does the override change, and why is that enough?**
It marks the order's status as modified whenever a line changed, so EF writes an `UPDATE` of the order row,
and that update carries the version check.

**4. What stops changing a submitted order's lines?**
`SetLines` starts with `EnsureDraft`, which throws on anything but a draft.

**5. Why share `SetLines` and `ResolveLinesAsync` there, not in a helper?**
The rules belong to the order (domain) and the loading belongs to the context; both are the natural homes.
A shared handler helper would tie two slices together through code that has no home of its own.

## Lesson 11 — Events and the outbox

**1. Why isn't "publish right after saving" good enough?**
A crash between the commit and the publish loses the event forever, and it works every time you test it.

**2. What does the outbox write, and in which transaction?**
An `outbox_message` row (type and JSON payload) for each event, in the same `SaveChanges`, so it commits with
the change.

**3. What does "at least once" mean, and what does it demand?**
An event may be delivered more than once. Every handler must be idempotent: running it twice must equal
running it once.

**4. What does `FOR UPDATE SKIP LOCKED` protect against?**
Two app instances taking and delivering the same messages. The second skips rows the first has locked.

**5. Why did `Money` come back as ₱0, and what fixed it?**
The serializer built the struct with its hidden empty constructor, and `Amount` has no setter. `[JsonConstructor]`
on the real constructor fixed it.

**6. Why does each module have its own outbox table?**
No SQL crosses a schema. Each module's event commits with its own changes, in its own schema.

## Lesson 12 — Testing

**1. Which bugs can't a unit test catch?**
Ones that depend on the database or the wiring: two requests racing, a transaction that doesn't really span
modules, a validator that isn't registered, a screen that crashes.

**2. Why drop the test database every run?**
So every run starts from the migrations and seed, and yesterday's leftovers can't change today's results.

**3. How does the race test make two reservations simultaneous?**
`Task.WhenAll` starts both at once, each in its own scope with its own connection and transaction, like two
real requests.

**4. How do you know the lost-update test protects anything?**
Remove the `SaveChangesAsync` override and watch it fail; put it back and watch it pass.

**5. Why does the lockout test use a throwaway user?**
Locking a dev user would break every other test that signs in as them.

**6. Two architecture rules the tests enforce.**
For example: a module references other modules only through their Contracts (no cycles, no reaching into
internals); and `InternalsVisibleTo` only to the host and tests (no module sees another's internals).

**7. Why only a few browser tests, and how do they find a button?**
They're slow and need a running app, so a few walk the main flows. They find things as a person does: by
label, by role and name, by visible text.

## Lesson 13 — Observability

**1. What does each signal answer?**
Logs: what happened, in words. Traces: where did the time go in this one request. Metrics: how much, how
often, how fast, over time.

**2. Why separate "rejected" from "error"?**
A blocked customer is the system working. Counting it as an error makes the error rate and its alerts
meaningless.

**3. Why did the telemetry behavior move to Infrastructure?**
It uses OpenTelemetry types that modules must not depend on. BuildingBlocks stays light.

**4. `/health/live` vs `/health/ready`?**
Live: the process answers. Ready: it can reach the database. A database blip shouldn't make something restart
a healthy process, only stop sending it traffic.

**5. Why is the Aspire dashboard only for development?**
It keeps everything in memory, forgets it on restart, and can't alert.

**6. How does a user's error report find the trace?**
The error body and the `X-Trace-Id` header carry the trace id; search for it.

## Lesson 14 — Customers

**1. Why add TIN, address and terms now, not in lesson 03?**
Nothing needed them until customers were maintained and invoiced. Introduce each thing when it's needed.

**2. Why does `Create` call `Update`?**
So creating and updating check exactly the same rules. A new customer is an update of an empty one.

**3. What does `search_customers` v2 do first, and why?**
It drops v1, because `CREATE OR REPLACE` can't change a function's result columns.

**4. Why does `search_customers.v1.sql` stay as it was?**
Lesson 07's migration reads it. On a fresh database, v1 is created, then v2 replaces it, in order.

**5. Who may block a customer, and where is that decided?**
SalesManager and Admin, at the endpoint, through `SalesRoles.Managers`.

**6. Why separate block and unblock endpoints?**
They're two business actions. Audit entries or notifications attach to exactly one of them.

## Lesson 15 — Users and email

**1. Why admin-created accounts, and how does the admin not know passwords?**
The company decides who works there and in which role. The admin sets a temporary password, and the person
must choose their own at first sign-in.

**2. Why disable, not delete?**
Their name is on history: orders, approvals, shipments. Disabled, they can't sign in, and the history stays.

**3. The three lockout-prevention rules, and where they live.**
Nobody disables themselves; nobody changes their own role; the last active admin can't be disabled or
demoted. In `StaffAccountRules`, a pure domain class.

**4. Identity's two extension points, and what each added.**
`CanSignInAsync`: refuse disabled users. `PasswordSignInAsync`: record every attempt in the sign-in log.

**5. When does a disabled person's browser stop working?**
Within a minute: the security stamp changed, and the cookie is checked against it every minute.

**6. Why "sent" even for an unknown email?**
Otherwise anyone could test which emails have accounts.

**7. How does the screen show a name for `created_by`?**
The app asks Identity's `GET /api/users/directory` and maps ids to names. No SQL crosses a schema.

## Lesson 16 — Item catalog and stock

**1. Who owns the name, who owns the price, and why not one module?**
Inventory owns what an item is; Sales owns what it costs and who may sell it. Each fact has one owner, so two
modules never fight over it.

**2. Why keep a copy instead of asking each time?**
Sales reads names and units constantly. A copy keeps Sales fast and working even if Inventory is busy, and
avoids SQL across schemas.

**3. Domain event vs integration event?**
A domain event is handled inside its module and is internal. An integration event is handled by other
modules; it lives in the Contracts project, public, with plain types.

**4. Why carry the full item?**
So the handler can apply it twice, or late, and still end up right: an idempotent upsert.

**5. Why take the id from a sequence first?**
The outbox writes the event during `SaveChanges`, before an identity column would assign the id. The event
must carry the real id.

**6. Sales never hears about a new item, with no error. First check?**
That the Contracts assembly is in the Mediator's `options.Assemblies`.

**7. Why does the seed end with `setval`?**
Explicit ids don't advance the sequence; without it, the first real item would collide with id 1.

## Lesson 17 — Shipping

**1. Why does the shipment belong to Inventory and the status to Sales?**
Moving stock is the warehouse's fact; whether the order is shipped is Sales' fact. Each changes only its own.

**2. Why doesn't the build refuse two-way calls?**
Each module references the other's Contracts project, never the module. Contracts reference only
BuildingBlocks, so there's no cycle.

**3. One line's reservation is missing: what happens?**
The guarded `UPDATE` matches nothing, the handler throws, and the transaction rolls back: no stock moves, and
the order stays approved.

**4. What stops a second shipment, twice over?**
Sales locks the order row, so the second clerk waits and then sees Shipped. And the unique index on
`shipment.sales_order_id` refuses a second shipment.

**5. Why can't a shipped order be cancelled?**
The goods are gone. Taking them back is a return, a different process.

**6. Why take the shipment's id from a sequence?**
Its number is built from the id, before the shipment exists, and later its event carries the id.

## Lesson 18 — Purchasing

**1. Who approves a purchase order, and why not the buyer?**
An admin. Spending needs a second pair of eyes.

**2. Why receive on Approved, before Sent?**
Suppliers deliver when they deliver; a missing click mustn't block the warehouse.

**3. Contract for Purchasing, copy for Sales: why?**
Purchasing reads item details rarely and needs them current; Sales reads them constantly. A copy pays off for
frequent reads, a contract for occasional ones.

**4. What keeps a receipt and the stock increase consistent?**
One transaction: the order records what arrived, and Inventory joins the transaction to raise on-hand and
write the movement.

**5. Two clerks count the same delivery: what stops double counting?**
The order's `xmin`, checked because `SaveChangesAsync` touches the order when lines change. The second gets a
409.

**6. Why is `PurchaseOrder` an `Entity`?**
It raises no events because nothing reacts to it. An event arrives with its first handler.

## Lesson 19 — Invoicing

**1. Why create the invoice by an event?**
So Inventory doesn't know Invoicing exists, and shipping keeps working even if Invoicing is down; the event
waits in the outbox.

**2. Why not a sequence, and what does the counter cost?**
A rolled-back sequence number is gone: a gap an auditor asks about. The counter row updates inside the
invoice's transaction, so a rollback gives it back. The cost: invoices are numbered one at a time.

**3. The same event twice at once: what stops a second invoice, and why no lost number?**
Both pass the first "already invoiced?" check. Taking a number locks the counter, so the second waits, then
finds the first's invoice and returns it; its transaction rolls back, and its number with it. The filtered
unique index is the last guard.

**4. Why copy customer details onto the invoice?**
An invoice is a legal snapshot. If the customer moves, old invoices must still show where the goods went.

**5. Why pin PDFsharp to 6.2.4?**
It's the last MIT release. 6.5 and later are AGPL-3.0, which would bind every buyer, and check for a license
at runtime.

**6. At 7 a.m. in Manila, what date?**
Today's local date, from `IClock.Today` in `Company:TimeZone`. In UTC it's still yesterday.

## Lesson 20 — Payments and receivables

**1. Why does Sales lower the balance itself?**
The balance is Sales' fact, and Invoicing may not change Sales' data. Invoicing announces the payment;
Sales reacts.

**2. What does `applied_payment` protect against, and how?**
Applying one payment twice. Its primary key is the payment id: a repeat finds the row and does nothing; a
racing duplicate fails the key and retries later.

**3. Owes ₱300, pays ₱500: what's deducted, and what comes back on void?**
₱300 is deducted (never below zero), and exactly ₱300 is given back.

**4. Why can't an invoice with payments be voided directly?**
The money must go back first. Voiding the payments tells Sales, then the invoice can be voided.

**5. What does the as-of date change, and which dates does it compare?**
It decides which invoices (issued by then) and payments (made by then) count, and each invoice's lateness is
as-of date minus due date.

**6. Why enums as names?**
Names can't be misread silently; a number from an older client could mean a different method.

## Lesson 21 — Security hardening

**1. Lockout vs rate limiting.**
Lockout protects one account from many guesses. A per-IP rate limit stops one password tried on many
accounts.

**2. Why the rate limiter after authentication?**
So it can count per user. Before, everyone behind one office IP would share one budget.

**3. What does HSTS add?**
The browser never even tries plain HTTP for a year, so nothing leaks before the redirect.

**4. Why no API key, and what for a non-person client?**
A key in a browser app is public. A non-person client gets its own Identity account and bearer tokens, or
OpenIddict for client credentials.

**5. Why keys in the database, then encrypted?**
In the database, they survive restarts and are shared by every instance. Encrypted with a certificate, a
stolen backup can't be used to forge sign-ins.

**6. What does `ALTER DEFAULT PRIVILEGES` save you?**
Granting `erp_app` rights after every migration. Whatever `erp_owner` creates is usable by `erp_app`
automatically.

**7. Why `'wasm-unsafe-eval'` and inline styles, but not inline scripts?**
Blazor can't run WebAssembly without the first; MudBlazor sets styles on elements. Inline styles can't run
code; inline scripts, the classic attack, stay forbidden.

## Lesson 22 — Docker

**1. What does an image contain, and what does the server need?**
The app with everything it needs to run: runtime, compiled code, fonts. The server needs only Docker Engine.

**2. Why a separate migrate container?**
A failing migration then stops the deploy, not every restart, and the app never needs rights to change
tables.

**3. Why no `ports` on `db`, and how do you look inside?**
So nothing outside the Docker network can reach it. Use `docker compose exec db psql -U postgres erp`.

**4. What does `ASPNETCORE_FORWARDEDHEADERS_ENABLED` fix, and why only behind Caddy?**
It makes the app see the real client IP and HTTPS scheme from Caddy's headers. Trusting those headers is
only safe when the proxy is the one way in.

**5. Why does `InitialAdmin` act only with no users?**
So a forgotten setting can't recreate a deleted account a year later.

**6. Backups: where, where else, and what else to restore?**
Nightly into the host's `backups` folder; also copied off the server daily. To restore a working system you
also need the data-protection certificate and its password (or everyone just signs in again).

## Lesson 23 — Deploy to IIS

**1. Why migrate as a deploy step?**
A bad migration shouldn't crash every restart, and the running app shouldn't have rights to change tables.

**2. Bundles run as `postgres`?**
Every object belongs to `postgres`, and `erp_app` gets `permission denied`: the default privileges were set
for `erp_owner`.

**3. Why settings in the app pool?**
`web.config` is rewritten on every publish, and it would put secrets in the deploy folder.

**4. Idle timeout and the outbox?**
IIS stops an idle app, and the dispatcher with it. Events wait safely in the outbox, but only until the next
request starts the app.

**5. Why no forwarded headers setting on IIS?**
ANCM passes IIS's client address and scheme to the app itself.

**6. 500.30: where first, and the usual causes?**
The Event Log. Usually a wrong connection string, an unreachable database, a missing or unreadable
data-protection certificate, or an initial admin password under 12 characters.

## Lesson 24 — Production checklist

**1. Why is an untested backup only a hope?**
You only know a backup works when you've restored it. Until then, it might be empty, broken or incomplete.

**2. Why keep the certificate apart from backups?**
Together they're the keys again: the backup holds the encrypted sign-in keys, the certificate unlocks them.

**3. Why items before prices, prices before orders?**
Sales' price list is filled from the catalog's events, and an item must be priced and on sale before it can
be ordered.

**4. What to confirm with a tax adviser?**
What the BIR requires to use the system's invoices officially, typically registering it as a Computerized
Accounting System and getting the invoice format approved.

**5. Where do logs go without an OTLP endpoint?**
Docker: `docker compose logs api` keeps recent ones. IIS: warnings and errors go to the Event Log; the rest is
lost.

**6. One left-out tool, its pain, and what stays.**
For example, a message broker: when another *system* must receive our events. The outbox stays; its
dispatcher forwards to the broker.
