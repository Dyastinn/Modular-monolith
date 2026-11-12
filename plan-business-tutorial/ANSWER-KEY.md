# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--testing) · [03](#lesson-03--mediator) ·
[04](#lesson-04--domain-model) · [05](#lesson-05--database) · [06](#lesson-06--sign-in) ·
[07](#lesson-07--create-an-order) · [08](#lesson-08--find-orders) · [09](#lesson-09--submit) ·
[10](#lesson-10--approve-reject-cancel) · [11](#lesson-11--update-an-order) ·
[12](#lesson-12--containers-and-ci) · [13](#lesson-13--events-and-the-outbox) ·
[14](#lesson-14--observability) · [15](#lesson-15--customers) · [16](#lesson-16--users-and-email) ·
[17](#lesson-17--item-catalog-and-stock) · [18](#lesson-18--shipping) · [19](#lesson-19--purchasing) ·
[20](#lesson-20--invoicing) · [21](#lesson-21--payments-and-receivables) · [22](#lesson-22--special-discounts) ·
[23](#lesson-23--security-hardening) · [24](#lesson-24--deploying-with-podman) ·
[25](#lesson-25--production-checklist)

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

## Lesson 02 — Testing

**1. What are the three parts of a test, and what does each one do?**
Arrange gets everything ready (a client, an order, a customer). Act does the one thing being tested. Assert
checks what happened. One act per test, so a failure points at one behavior.

**2. Why do we break the code on purpose after writing a test?**
A test that has never failed might not be able to fail: it could check the wrong thing, or nothing. Seeing it
go red when the code breaks, and green when it's fixed, is the only proof it guards what its name says.

**3. What does `WebApplicationFactory` do, and why doesn't it need a port?**
It runs our real `Program.cs` inside the test process, with an in-memory server instead of a network
listener. `CreateClient()` returns an `HttpClient` wired straight to that server, so requests go through the
real middleware and routing without touching the network.

**4. When would you write a `[Theory]` instead of a `[Fact]`?**
When the same check should hold for several inputs: every HTTP method, every boundary value. One theory with
`[InlineData]` rows instead of copies of one test, and each row reported on its own.

**5. Pick three rows of the edge-case checklist and give an example of each for a sales order.**
For example: *at the limit*, an order of exactly ₱50,000 (approved) and ₱50,000.01 (waits for a manager);
*twice*, pressing Submit twice; *who*, a rep opening another rep's order (404). Any three with a concrete
order example are right.

**6. What is a regression test? Name the one in this lesson and the bug it guards against.**
A test written after a bug so it can't come back. `The_component_library_files_are_served` guards against the
`ReferenceOutputAssembly="false"` reference that once left the app without MudBlazor's CSS and JS.

**7. Why should most tests be unit tests, and only a few drive a browser?**
Unit tests run in microseconds, never flake, and a failure names one rule. Browser tests need a running app,
take seconds each, and break for reasons unrelated to the rule (timing, layout). Test rules underneath; keep a
few browser tests for the main flows.

## Lesson 03 — Mediator

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

**6. The unit test for `PingValidator` passes, but the API still answers `pong: ` to an empty message. What's
wrong, and which test catches it?**
The validator works but never runs: most likely `includeInternalTypes: true` is missing, so
`AddValidatorsFromAssembly` never registers it (or the behavior isn't in the pipeline). The integration test
`An_empty_message_never_reaches_the_handler` catches it, because it sends a real request.

**7. What does an architecture test check that the compiler doesn't? Give two examples.**
Rules about how the code is built: a module exposes only its entry class; `InternalsVisibleTo` goes only to the
host and tests; code is grouped by feature, not by technical layer. The compiler accepts all of those
violations.

## Lesson 04 — Domain model

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

**7. Name three edges of "an order over ₱50,000 needs a manager", and what each test checks.**
Exactly ₱50,000 (auto-approved, because the rule says *above*), ₱50,000.01 (waits for a manager), and a
discount bringing a bigger order down to the threshold or below (the rule uses `Total`, after discounts). The
first two are `An_order_of_exactly_50000_is_approved_on_submit` and `One_centavo_over_50000_waits_for_a_manager`.

**8. What does a property-based test give you that a `[Theory]` with ten cases doesn't? What does it cost?**
It tries a hundred inputs nobody picked, including ones you'd never think of, and shrinks a failure to the
simplest input. The cost: it's harder to write, a poor generator tests nothing interesting, and it catches
different bugs from examples (the `ToEven` rounding change passed the properties and failed the theory).

**9. Why does `SetLines` build the new lines aside before replacing the old ones? Which test proves it?**
So a refused line leaves the order exactly as it was, instead of half-changed. `A_failed_change_of_lines_keeps_the_old_lines`.

## Lesson 05 — Database

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

The test that would catch such a key: `No_foreign_key_crosses_a_module_boundary` (question 6).

**7. Why do the tests use their own database, and why is it dropped at the start of every run?**
So a test can't change or delete your development data, and so every run starts from the migrations and the
seed, not from whatever the last run left behind.

**8. Why does the round-trip test read the order back in a second scope?**
In the same scope, EF Core would return the object it already has in memory, and the test would prove nothing
about the columns. A new scope means a new `DbContext` that really reads the rows.

**9. The architecture test failed after the first migration. Was that a bug? What changed in the test, and
why?**
Not a bug in the module: EF Core generates migrations as public classes, and they must stay public. The test
learned the exception, with its reason: types in a `.Migrations` namespace are left out.

## Lesson 06 — Sign-in

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

**9. Why does the lockout test use a throwaway user instead of `rep@erp.local`?**
Locking the rep for fifteen minutes would make every later test that signs in as the rep fail, for a reason
that has nothing to do with them. Tests must not step on each other.

**10. Why must an unknown email get exactly the same answer as a wrong password?**
Different answers let an attacker try emails and learn which ones are staff accounts, then aim at those.

**11. How does a browser test find the "Sign in" button, and why not by its CSS class?**
By its role and accessible name (`GetByRole(AriaRole.Button, new() { Name = "Sign in" })`), the way a person
finds it. A CSS class is a styling detail that changes for reasons unrelated to whether the button works.

## Lesson 07 — Create an order

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

**7. Half a widget is a 422, but a quantity of 1.2345 is a 400. Why the difference?**
1.2345 breaks the request's shape for every item (at most three decimals): that's the validator, a 400. 1.5
is a fine quantity in general, but widgets are counted in whole pieces: that's the item's business rule, a 422.

**8. Why does each edge-case test create its own customer?**
Tests share one database, and orders change balances. A fresh customer means a test's credit and balance
can't depend on which tests ran before it.

## Lesson 08 — Find orders

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

The browser test `Every_page_opens_without_an_error` catches the copies drifting (question 7).

**8. A user searches for `50%`. What did the first version return, and how does `SearchText.ForLike` fix it?**
Every order whose number or customer name contains "50", because `%` is a wildcard inside `LIKE`. `ForLike`
escapes `%`, `_` and `\`, so they're matched as the characters they are.

**9. Why does the status filter compare names instead of calling `Enum.TryParse`?**
`Enum.TryParse` accepts any number, even one that's no status at all (`"99"`), so the search would silently
return nothing instead of saying the filter is wrong.

**10. Which test keeps the C# total and the SQL total in agreement, and why pick a quantity like 1.333?**
`The_list_shows_the_same_total_the_domain_computes`. 620.50 × 1.333 = 827.1265 has to be rounded, so it shows
whether both sides round the same way, at the same point.

## Lesson 09 — Submit

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

**7. How does the race test make two reservations arrive at the same instant, and why does each get its own
scope?**
`Task.WhenAll` starts both before waiting for either. Each runs in its own DI scope, so each has its own
connection and transaction, exactly like two separate requests.

**8. A ₱1,500 order is approved and charged before its stock check fails. Which test proves the charge
disappears, and what does it check?**
`Too_little_stock_refuses_the_submit_and_leaves_everything_as_it_was`: after the 422 the order is still a
draft, the stock's `reserved` is unchanged, and the customer's balance is zero (from lesson 13, also that no
outbox row exists).

**9. Name the three architecture tests this lesson adds, and what each one prevents.**
`A_module_references_other_modules_only_through_their_Contracts` (modules tangled together),
`Contracts_reference_no_module_so_no_cycle_can_form` (a reference cycle), and
`Only_the_host_references_shared_infrastructure` (a module reaching past the interfaces to the implementations).

## Lesson 10 — Approve, reject, cancel

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

**7. Two managers approve at once. Why does the test accept either 409 or 422 for the loser?**
Both are correct, depending on timing: if both loaded the order before either saved, the version check refuses
the second (409); if the first committed before the second loaded, the domain refuses to approve an approved
order (422). The test checks every correct outcome: exactly one approval, one charge.

**8. How would you know the `xmin` mapping really protects anything?**
Remove it and run the race test a few times: sometimes both approvals win and the customer is charged twice.
Put it back, and the test always passes.

## Lesson 11 — Update an order

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

**6. How do two scopes in one test act like Ana and Bob? Why not just send two HTTP requests?**
Each scope has its own `DbContext`, so each loads the draft before either saves: the exact moment that
matters. Two HTTP requests would almost never overlap that precisely.

**7. Coverage is 95% for a class. Does that mean its rules are tested? What would tell you more?**
No: coverage only says the lines ran. A test that checks nothing covers them too. Mutation testing tells you
more: whether a test would notice if a line were wrong.

**8. A mutant survived. What does that mean, and what do you do about it? When is it fine to leave one?**
A change to the code that no test noticed: a rule without a test. Write the test. It's fine to leave one that
only changes an error message's wording, or removes a check another check already makes.

## Lesson 12 — Containers and CI

**1. Name three problems the shared `erp_test` database caused.**
The tests dropped a database on whatever server was configured; two runs at once dropped it from under each
other; every machine needed the same PostgreSQL installed; a fresh machine (like a CI server) couldn't run the
tests at all.

**2. What is the difference between an image and a container? And a volume?**
An image is the frozen package: a program and everything it needs. A container is a running copy of an image.
A volume is storage outside the container, so data survives when the container is removed.

**3. Give two reasons the course uses Podman. Would anything change if your company uses Docker?**
It's free for any use on every system (Apache-2.0), and it runs rootless with no background service as root.
Nothing changes with Docker: the images and files follow the open standards, and Testcontainers and Compose
work with either.

**4. Why did no test change when the factory moved to Testcontainers?**
Every test reached the database through `factory.OpenDatabase()`, so only the factory knew where the database
was.

**5. What does Ryuk do, and when does it matter?**
It's Testcontainers' cleaner: it removes the test containers even if the test run crashes or is stopped
halfway, so they don't pile up.

**6. What happens, step by step, when you push to GitHub?**
GitHub starts the workflow on a fresh Ubuntu machine: fetch the code, install .NET, restore the tools, build,
run every test (Testcontainers starts PostgreSQL on the machine's Docker), make the coverage report, and show a
green tick or a red cross (and email you on red).

**7. Why does the coverage step say `if: always()`?**
So the report is made even when a test failed, which is exactly when you want to see what was and wasn't run.

**8. Why don't the browser tests run in CI?**
They need a running app, and they're the slowest and most fragile tests. They run on your machine before a
release.

## Lesson 13 — Events and the outbox

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

**7. How does a test wait for something that happens on a background timer, without a fixed delay?**
It polls: check, wait a short moment, check again, up to a limit. Fast when the system is fast, patient when
the machine is busy, and never flaky because of one guessed delay.

**8. How does `Delivering_every_event_a_second_time_changes_nothing` imitate a crash, and what does it prove?**
It sets `processed_utc` back to `NULL` on every delivered message, which is what a crash between "handler ran"
and "marked as done" looks like. When everything is delivered again and the business snapshot is unchanged,
every handler is idempotent.

**9. Why must a test pass when it's run alone? What went wrong the first time this lesson's tests were?**
Because you'll run it alone while debugging, and because test order isn't guaranteed. Run alone, the
redelivery test had no events to redeliver (it relied on earlier tests), and a test that only read the
database found no schema, because the app hadn't started yet.

## Lesson 14 — Observability

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

**7. How do we test that the ready check fails, without stopping the tests' database?**
By running the health check on its own, against a data source pointed at an address where nothing listens
(port 1), with a short timeout.

**8. Why must every error body, even the framework's own, carry the same trace id as the header?**
So a user's report leads to exactly one trace in the logs. Two formats of one id means searching for the wrong
one.

## Lesson 15 — Customers

**1. Why add TIN, address and terms now, not in lesson 04?**
Nothing needed them until customers were maintained and invoiced. Introduce each thing when it's needed.

**2. Why does `Create` call `Update`?**
So creating and updating check exactly the same rules. A new customer is an update of an empty one.

**3. What does `search_customers` v2 do first, and why?**
It drops v1, because `CREATE OR REPLACE` can't change a function's result columns.

**4. Why does `search_customers.v1.sql` stay as it was?**
Lesson 08's migration reads it. On a fresh database, v1 is created, then v2 replaces it, in order.

**5. Who may block a customer, and where is that decided?**
SalesManager and Admin, at the endpoint, through `SalesRoles.Managers`.

**6. Why separate block and unblock endpoints?**
They're two business actions. Audit entries or notifications attach to exactly one of them.

## Lesson 16 — Users and email

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

## Lesson 17 — Item catalog and stock

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

## Lesson 18 — Shipping

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

## Lesson 19 — Purchasing

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

## Lesson 20 — Invoicing

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

**7. The VAT rate changes. Who changes it, where, and what happens to old invoices?**
The accountant, on **Invoicing → Company and tax**: no developer, no restart, and the change records who made
it. Invoices issued before keep the rate, breakdown and seller details they stored at issue; only new invoices
use the new rate.

**8. Why does the order line copy the VAT treatment, and why is `TaxType` in BuildingBlocks?**
The invoice bills what was ordered, so a later change in the price list can't change an order already
placed. And it crosses Sales' service window in `InvoiceableLine`; a Contracts project references only
BuildingBlocks, so that's where a type both sides share must live.

**9. Why do enums travel as names?**
Names can't be misread silently; a number from an older client could mean a different VAT treatment.

## Lesson 21 — Payments and receivables

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

**6. ₱891.11 paid and ₱8.89 withheld on a ₱900 invoice: paid? What's still needed?**
Yes, paid: tax withheld settles the invoice like money, and Sales lowers the balance by ₱900. The business still
needs the customer's BIR Form 2307 for ₱8.89, to claim it against its income tax. **Withholding tax (2307)**
shows it as missing until the certificate is recorded.

## Lesson 22 — Special discounts

**1. Why does the order compute the discount, not the invoice?**
The customer's balance is charged with the order's total at approval, and payments settle the invoice. If only
the invoice were discounted, the two would never meet. One shared calculation gives both the same total.

**2. A senior buys a ₱560 VAT-inclusive medicine.**
Take out the VAT: ₱560 ÷ 1.12 = ₱500. Take 20% of that: ₱100. The senior pays ₱400.

**3. Why store the VAT rate the discount removed?**
So the invoice, issued later, works out exactly the same amounts as the order, even if the company's VAT
setup changed in between.

**4. Why store the two amounts on the order?**
Three SQL functions add up order totals, and SQL can't call the C# calculation. Stored amounts let them
subtract instead of recalculating.

**5. What must the invoice show for a special discount?**
The holder's ID number, the discount broken down into the VAT exemption and the discount itself, and a space
for the holder's signature (RR 7-2024 Sec. 6(B.18)).

**6. Who decides which items qualify, and where is it recorded?**
The business, with its tax adviser, since the law decides what's covered. A sales manager records it per item
in the price list, and the order line copies it.

## Lesson 23 — Security hardening

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

## Lesson 24 — Deploying with Podman

**1. What does an image contain, and what does the server need?**
The app with everything it needs to run: runtime, compiled code, fonts. The server needs only Podman (or Docker Engine).

**2. Why a separate migrate container?**
A failing migration then stops the deploy, not every restart, and the app never needs rights to change
tables.

**3. Why no `ports` on `db`, and how do you look inside?**
So nothing outside the stack's private network can reach it. Use `podman compose exec db psql -U postgres erp`.

**4. What does `ASPNETCORE_FORWARDEDHEADERS_ENABLED` fix, and why only behind Caddy?**
It makes the app see the real client IP and HTTPS scheme from Caddy's headers. Trusting those headers is
only safe when the proxy is the one way in.

**5. Why does `InitialAdmin` act only with no users?**
So a forgotten setting can't recreate a deleted account a year later.

**6. Backups: where, where else, and what else to restore?**
Nightly into the host's `backups` folder; also copied off the server daily. To restore a working system you
also need the data-protection certificate and its password (or everyone just signs in again).

**7. Why does the ERP run as its own ordinary user, and which two things does rootless Podman need allowed?**
So a break-in gets one ordinary user's rights, not root's. It needs ports 80 and 443 opened to ordinary users
(the `sysctl` line) and lingering, so the containers keep running after that user logs out.

**8. A buyer already runs Docker. What do they change to install your ERP?**
They type `docker compose` instead of `podman compose`. The `Dockerfile` and `compose.yaml` are the same.

**9. Why does the first-run test check that `admin@erp.local` *can't* sign in?**
If the development seed ever ran in production, anyone who read the course could sign in as admin with a
known password. The test proves a production install has no development users.

## Lesson 25 — Production checklist

**1. Why is an untested backup only a hope?**
You only know a backup works when you've restored it. Until then, it might be empty, broken or incomplete.

**2. Why keep the certificate apart from backups?**
Together they're the keys again: the backup holds the encrypted sign-in keys, the certificate unlocks them.

**3. Why items before prices, prices before orders?**
Sales' price list is filled from the catalog's events, and an item must be priced and on sale before it can
be ordered.

**4. What to do with the BIR first, and where do the results go?**
Register the system as a Computerized Accounting System, with a tax adviser, before the first invoice. The
BIR's permit number (or acknowledgement certificate number), the approved series range and the date issued go
into **Invoicing → Company and tax**, and every invoice issued after that prints them at the bottom.

**5. Where do logs go without an OTLP endpoint?**
`podman compose logs api` keeps recent ones, and nothing more. For search, history and alerts, set
`OTEL_EXPORTER_OTLP_ENDPOINT`.

**6. One left-out tool, its pain, and what stays.**
For example, a message broker: when another *system* must receive our events. The outbox stays; its
dispatcher forwards to the broker.
