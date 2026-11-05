# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--mediator) · [03](#lesson-03--domain-model) ·
[04](#lesson-04--database) · [05](#lesson-05--sign-in) · [06](#lesson-06--create-an-order) ·
[07](#lesson-07--submit) · [08](#lesson-08--approve-reject-cancel) · [09](#lesson-09--update-an-order) ·
[10](#lesson-10--reads) · [11](#lesson-11--events-and-the-outbox) · [12](#lesson-12--observability) ·
[13](#lesson-13--security-hardening) · [14](#lesson-14--testing) · [15](#lesson-15--deploy-to-iis) ·
[16](#lesson-16--production-checklist)

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

## Lesson 02 — Mediator

**1. Why does the generator need `InternalsVisibleTo("Erp.Api")`? Why is that safe for the host but not for
another module?**
The source generator runs in `Erp.Api` and writes code that names each module's internal commands and
handlers; without access, that's `CS0122`. The host already references every module and only wires them
together. Another module with that access could reach inside, and the island walls would be gone.

**2. You forget `includeInternalTypes: true`. What happens on `GET /ping?message=`?**
The internal validator is never found, so it never runs. The empty message goes straight to the handler and
you get a 200 instead of a 400. Nothing warns you.

**3. Why is `LoggingBehavior` registered before `ValidationBehavior`?**
The first registered is the outermost. Logging wraps validation, so rejected requests are logged and timed
too.

**4. What's the difference between a command and a query, and why tell them apart?**
A command changes something; a query only reads. Telling them apart shows at a glance what has side effects,
and later lets each side use the right tool: EF Core for writes, Dapper for reads.

**5. Name one real cost of the mediator pattern.**
Indirection: you can't jump from an endpoint straight to its handler with "Go to definition". Also one more
concept to learn, and the generator's `InternalsVisibleTo` setup.

## Lesson 03 — Domain model

**1. Why does `Customer.EnsureWithinCreditLimit` take `otherPendingTotal`? Describe the bug without it.**
Orders waiting for a manager haven't been charged yet. Without them, each order is checked alone: a customer
with ₱100,000 of credit could have three ₱90,000 orders pending at once, each passing, and owe ₱270,000 once
they're approved.

**2. Where is "an order over ₱50,000 needs a manager" enforced, and how does a small order skip the
manager?**
In the domain: `SalesOrder.Submit` uses the approval policy. At ₱50,000 or less, `Submit` approves the order
on the spot and charges the customer; above it, the order waits in `PendingApproval`.

**3. Why does `Reject` return the order's lines, instead of the handler deciding what to release?**
Only the order knows whether it was holding stock. If the handler decided, a business `if` would leak out of
the domain. The handler just releases what it's given.

**4. What stops a handler from setting `order.Status = SalesOrderStatus.Approved` on a draft?**
`Status` has a private setter. The only way to change it is a method like `Approve`, which checks the
transition is allowed.

**5. Why is `CustomerId` a struct and not a `long`?**
So the compiler refuses an `ItemId` where a `CustomerId` belongs. A `readonly record struct` costs nothing at
runtime and compares by value.

**6. Why does approving charge the customer inside the domain, and not in the handler?**
Approving and charging must always happen together. In the domain, they're one method, so no handler can do
one and forget the other, and a unit test proves it.

## Lesson 04 — Database

**1. Why does each module get its own DbContext and its own schema?**
Each module owns its data. Its own schema means no other module's tables to join to, and its own DbContext
and migration history mean it can change its tables without touching anyone else.

**2. What does `Configure(...)` protect you from, and who calls it?**
From the running app and the `dotnet ef` tool configuring the context differently, for example one with
snake_case names and one without, which would produce migrations that don't match the app. The module's
registration calls it at runtime, and the design-time factory calls it for `dotnet ef`.

**3. Why does `Customer` need a private parameterless constructor now?**
When EF Core loads a saved customer, it creates the object without running our rules, because loading isn't
a new business event. It needs a constructor with no parameters for that; private is enough. Last lesson,
nothing loaded customers from a database.

**4. NuGet resolves "10.0.4 or newer" to which version, and why did we pin EF Core?**
The lowest that fits: 10.0.4. Other packages pulled 10.0.14, so one app would have mixed two versions of EF
Core. Pinning `Microsoft.EntityFrameworkCore.Relational` with transitive pinning makes everything use 10.0.14.

**5. Why does the seed script use `WHERE NOT EXISTS`?**
It runs on every start in development. It must add what's missing and change nothing when run again.

**6. Why is there no foreign key from `sales_order.created_by` to a users table?**
Users belong to the Identity module, in another schema. A foreign key across schemas ties two modules
together at the database level. We store the id and nothing more.

## Lesson 05 — Sign-in

**1. Name three things production sign-in needs that a quick hand-written version usually forgets.**
Any three of: slow, salted, upgradable password hashing; constant-time comparison; lockout after repeated
wrong passwords; signing everyone out when a password changes; short-lived tokens with refresh; safe
cookies; expiring reset links.

**2. Why do we block `/auth/register`, and how?**
Staff accounts are created by an administrator, never by strangers signing themselves up. An endpoint filter
on the `/auth` group answers 404 for `/auth/register`.

**3. A browser and a mobile app both call the API. How does each stay signed in?**
The browser keeps a cookie (`SameSite=Strict`, sliding expiry). The mobile app keeps a short-lived bearer
token plus a refresh token, and trades the refresh token at `/auth/refresh` for a new pair.

**4. Why a 12-character minimum with no "must contain a digit" rule?**
Length makes a password hard to guess; composition rules mostly produce predictable patterns like
`Password1!`. That's the current NIST guidance (SP 800-63B).

**5. What does "sign out everywhere" change, and why does that end every session?**
It changes the user's security stamp. Every cookie and refresh token carries the old stamp, so the next
time one is checked, it no longer matches and is refused.

**6. Why does `CurrentUser` live in Infrastructure, while `ICurrentUser` lives in BuildingBlocks?**
Modules only need to ask "who is calling?", so the interface goes where they can see it. Reading claims
from the HTTP request is an ASP.NET Core detail, so the implementation goes where only the host can see it.
Handlers stay testable without HTTP.

## Lesson 06 — Create an order

**1. Validator or domain: "quantity is positive", "customer isn't blocked", "at most 5 Controlled items per
order"?**
"Quantity is positive" is shape: validator. "Customer isn't blocked" and "at most 5 Controlled items" are
business rules: domain.

**2. Why does `MAX(order_number) + 1` go wrong, and how does a sequence avoid it?**
Two requests read the same maximum at the same moment and both use the next number. A sequence hands out
each number exactly once, atomically, without locking anything.

**3. Why is there a `CreateSalesOrderRequest` *and* a `CreateSalesOrderCommand`?**
The request is the HTTP contract, what callers send; the command is the internal message. Today they match.
The day you rename an API field, or add something the command mustn't trust from the caller, they can differ
without breaking each other.

**4. What status does each produce: a blocked customer, an empty order, an unknown item?**
A blocked customer is a broken business rule: 422. An empty order is a malformed request: 400, from the
validator. An unknown item doesn't exist: 404.

**5. Why does the error body include a `traceId`?**
So a user can quote it, and you can find every log line and trace for that exact request.

**6. Why is Swagger only turned on in Development?**
It's a map of every endpoint. Handy for you, handy for an attacker too.

## Lesson 07 — Submit

**1. Why is stock reserved at submit, and not at approval?**
Approval can take days. Without a reservation, the stock could be promised to someone else in the meantime,
and the approved order couldn't be filled.

**2. What goes wrong with "read the stock, check it in C#, then save"? What fixes it?**
Two submits read "1 available" at the same moment, both pass, both save: one unit sold twice. The fix is
one atomic `UPDATE` with the check in its `WHERE` (`ExecuteUpdate`), and treating zero rows changed as "not
enough stock".

**3. Why does Sales reference `Erp.Modules.Inventory.Contracts`, and not `Erp.Modules.Inventory`?**
The Contracts project is Inventory's one service window. Sales can ask for a reservation, but can't touch
Inventory's tables, domain or DbContext.

**4. Why did every module's DbContext move onto one shared connection?**
A transaction belongs to a connection. Inventory can only join Sales' transaction if its DbContext uses the
same connection.

**5. The third of three lines is out of stock. What happens to the first two reservations?**
They're rolled back. All three run in Sales' transaction; the third throws, the transaction never commits,
and the order stays a draft.

**6. Two reps submit for the same customer at the same instant. What does `FOR UPDATE` make the second one
do?**
Wait. It can't lock the customer row until the first transaction ends, and then it sees the first order in
the pending total.

## Lesson 08 — Approve, reject, cancel

**1. Describe a lost update, using two approvals for the same customer.**
Two managers approve two orders at once. Both load the customer with a balance of ₱0, each adds their
order's total, and both save. The second save overwrites the first, and one order's charge disappears.

**2. What does EF Core add to its `UPDATE` once `xmin` is a row version, and what happens when it matches no
row?**
It adds `AND xmin = <the value it loaded>` to the `WHERE`. If someone changed the row since, no row matches,
EF throws `DbUpdateConcurrencyException`, and the API answers 409.

**3. Why does `ApproveSalesOrderHandler` need no `BeginTransactionAsync`, while `CancelSalesOrderHandler`
does?**
Approve changes only Sales data, in one `SaveChangesAsync`, which is atomic on its own. Cancel also releases
stock through Inventory's contract, so two contexts must commit together.

**4. "Only managers approve" and "not your own order" live in different places. Where, and why?**
"Only managers" is a fixed role check: the endpoint. "Not your own order" depends on the data, who created
this order: the domain.

**5. Why is the rejection reason checked by both the validator and the domain?**
The validator gives the caller a fast, clear 400 for a malformed request. The domain protects the rule
itself, whoever calls it.

**6. When would you lock a row up front instead of using a version check?**
When conflicts are likely, or a retry is expensive, like the credit check on submit. A version check is
optimistic: cheap when conflicts are rare.

## Lesson 09 — Update an order

**1. Why does update replace the whole list of lines, instead of patching it?**
The client sends the order as it should be. There are no per-line operations to get wrong, and the domain
checks its rules against the complete set.

**2. Without the `SaveChangesAsync` override, why doesn't the `xmin` check catch Ana and Ben?**
Lines live in their own table. Changing only lines updates `sales_order_line` rows, never the
`sales_order` row, so its `xmin` is never checked.

**3. What does the override change, and why is that enough?**
When any line changed, it marks the order itself as modified. EF then updates the order row too, with the
`xmin` check, so the second save fails with 409.

**4. Why is `order.ClearLines()` the first thing the handler does to the order?**
It's also the "drafts only" check. A submitted order throws right there, before anything else is loaded.

**5. Create and update share a loop of code. Why is that acceptable, and when would it stop being?**
Each slice owns its code, so changing update can never break create. When the two copies must change
*together*, that's a shared rule, and it moves into the domain.

## Lesson 10 — Reads

**1. Why don't the query handlers load `SalesOrder` aggregates with EF Core?**
A screen needs a shape, not an aggregate: joined names, a page, a total. A SQL function returns exactly that,
with no change tracking. Aggregates exist to protect writes.

**2. Why are the reads PostgreSQL *functions* rather than stored procedures?**
PostgreSQL procedures can't return rows. Functions with `RETURNS TABLE` can.

**3. A function needs a new column. What files do you add, and what must you never do?**
Add a `.v2.sql` file and a new migration that applies it. Never edit the shipped `.v1` file or a migration
that has already run somewhere.

**4. What is `MatchNamesWithUnderscores` for, and why is it set in Infrastructure?**
It maps `customer_name` to `CustomerName`. It's a global Dapper setting, so it's set once at startup, in the
one place that configures Dapper.

**5. Why is the order count a separate function from the search?**
So a page past the end still reports the real total. A count computed over an empty page would say zero.

**6. What happens on `GET /sales-orders` if `page` is `int` instead of `int?`?**
A non-nullable query parameter is required, so a request without `?page=` fails with a 400 before the
handler runs.

## Lesson 11 — Events and the outbox

**1. Why is "publish the event right after saving" not good enough?**
If the process dies between the save and the publish, the event is lost forever. Publish before the save
instead, and a rollback leaves an event about something that never happened.

**2. What exactly does the outbox write, and in which transaction?**
One row per event in the module's `outbox_message` table: its type, its JSON and when it happened. The
interceptor writes it in the same `SaveChanges`, so in the same transaction as the change itself.

**3. What does "at least once" mean, and what does it demand from every handler?**
An event may be delivered more than once, for example when the process stops after handling it but before
marking it done. Every handler must be idempotent: handling it twice must be the same as handling it once.

**4. What does `FOR UPDATE SKIP LOCKED` protect against?**
Two dispatchers, on two servers or in two overlapping loops, taking the same messages. Rows locked by one are
skipped by the other.

**5. Why did `Money` come back as ₱0, and what fixed it?**
The JSON serializer created `Money` through the struct's default constructor and couldn't set its private
`Amount`. `[JsonConstructor]` on the validating constructor fixed it, and the round-trip test proves it.

**6. Why does each module have its own outbox table?**
The event must be saved in the same transaction as the module's change, through the module's own DbContext,
in its own schema. A shared table would mean writing across schemas.

## Lesson 12 — Observability

**1. What question does each signal answer: logs, traces, metrics?**
Logs: what happened? Traces: where did the time go in this one request? Metrics: how much, how often, and is
it getting worse?

**2. Why does the telemetry behavior separate "rejected" from "error"?**
A rejection (validation, a business rule, not found, a conflict) is the caller's problem and expected. An
error is our bug. You alert on errors; alerting on rejections is noise.

**3. Why did the telemetry behavior move from BuildingBlocks to Infrastructure?**
It depends on OpenTelemetry, and BuildingBlocks must stay light, because every module references it. Only the
host registers behaviors anyway.

**4. What's the difference between `/health/live` and `/health/ready`, and why does it matter?**
Live means the process is running. Ready means it can do its job, with the database reachable. A database
outage should stop traffic, not make something restart a healthy app over and over.

**5. Why is the Aspire dashboard only for development?**
It keeps everything in memory, forgets it on restart, and can't alert anyone.

**6. How does a user's error report help you find the right trace?**
Every response carries `X-Trace-Id`, and every error body a `traceId`. Search for it, and you get that
request's whole trace and every log line.

## Lesson 13 — Security hardening

**1. What does lockout stop that rate limiting doesn't, and the other way round?**
Lockout protects one account from many guesses, wherever they come from. A per-IP rate limit stops one
source trying a password across many accounts, which never trips any single account's lockout.

**2. Why must the rate limiter come after authentication?**
Before authentication, every caller is anonymous and counted by IP. Everyone in one office would share one
budget.

**3. What does HSTS add on top of redirecting to HTTPS?**
The browser remembers, for a year, to use HTTPS for this site. After the first visit, it never sends a plain
HTTP request at all, so there's nothing to intercept.

**4. What's the difference between an API key and a bearer token?**
The key says which *program* is calling; the token says which *user*. In a browser app the key isn't
secret, so it never protects data on its own.

**5. Why must sign-in keys move into the database before a real server?**
By default they live in the user profile. An IIS app pool may have no profile, so every restart would sign
everyone out, and two servers would each have their own keys.

**6. What does `ALTER DEFAULT PRIVILEGES` save you from doing after every migration?**
Granting `erp_app` rights on every new schema, table, sequence and function by hand.

## Lesson 14 — Testing

**1. Which bugs can't a unit test catch, and why?**
Bugs in how the pieces fit: SQL, EF mappings, transactions, DI registrations, middleware order, sign-in,
races. A unit test leaves all of those out.

**2. Why is the test database dropped at the start of every run?**
Every run starts from migrations and seed, so nothing depends on leftovers, and every run proves the
migrations still work from nothing.

**3. How does the race test make two reservations happen at the same time?**
It starts both submits without awaiting either, then awaits both with `Task.WhenAll`.

**4. How would you know the lost-update test actually protects anything?**
Break the code it guards, for example remove the root touch, and watch it go red.

**5. Why does the lockout test use a throwaway user?**
Locking a shared seed user would break every other test that signs in as them.

**6. Name two architecture rules the tests enforce, and what each one protects.**
Any two of: modules reference other modules only through Contracts (the islands); Contracts reference no
module (no cycles); only the host references shared infrastructure; a module exposes only its entry class;
internals are shared only with the host and tests; the domain depends on no data access or web code.

## Lesson 15 — Deploy to IIS

**1. Why do migrations run as a deploy step instead of when the app starts?**
Changing the schema needs owner rights, and the running app should only have row rights. A routine restart
shouldn't run a migration nobody is watching, and two servers shouldn't race to run one. At deploy time, a
person runs it, with the site offline, and sees if it fails.

**2. What goes wrong if the migration bundles run as `postgres` instead of `erp_owner`?**
Everything they create belongs to `postgres`. The default privileges only cover what `erp_owner` creates, so
`erp_app` gets `permission denied`.

**3. Why do the settings live in the app pool and not in `web.config`?**
`dotnet publish` rewrites `web.config`, so settings there vanish on the next deploy, and secrets would sit in
the deploy folder. App pool variables live in IIS's own configuration, outside it.

**4. What does IIS's idle timeout do to the outbox, and what happens to events raised while the app is
stopped?**
It stops the process after 20 quiet minutes, and the dispatcher with it. Nothing is lost: events wait in the
outbox table and go out when the app starts again. But they'd wait for a visitor, so we turn the timeout off.

**5. Why does `InitialAdmin` only act when there are no users at all?**
So a forgotten setting can never recreate an account later, for example after that admin was deleted, with a
password sitting in a config file.

**6. A server answers 500.30. Where do you look first, and what are the usual causes?**
The Event Log: Windows Logs, Application, source *IIS AspNetCore Module V2*. Usually a wrong connection
string, an unreachable database, or an `InitialAdmin` password under 12 characters.

## Lesson 16 — Production checklist

**1. Why is a backup that's never been restored only a hope?**
Until you restore it, you don't know the file is complete, readable, or of the right database. The drill
turns "we have backups" into "we can recover".

**2. Why does the backup run as `erp_owner`, and why must the roles exist before a restore?**
`erp_owner` owns every object, so it can read all of them. Roles belong to the server, not the database, so
the dump doesn't create them; the restore needs `erp_owner` to own the objects and `erp_app` to receive its
rights.

**3. Which missing piece would a customer notice first, and why?**
Maintaining customers and items: a fresh install starts empty, so nobody can place an order. (Screens are a
good answer too: nobody runs a business from PowerShell.)

**4. Why is "who sees which orders" a decision rather than a bug, and where would the rule live?**
Some businesses want every rep to see every order; others don't. It's policy, not correctness. If needed, it
depends on the data, so it lives in the handler, using `ICurrentUser`.

**5. Under IIS, where do the app's logs go if no OTLP endpoint is set?**
Console output goes nowhere. Warnings and errors reach the Windows Event Log; everything else is lost.

**6. Name one tool this course left out, the pain that would justify it, and what stays when you add it.**
For example, a message broker: when another *system* must receive our events. The outbox stays; its
dispatcher forwards events to the broker instead of handling them in-process.
