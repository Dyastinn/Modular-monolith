# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

The course grows with the product, so this page grows with it: each lesson's answers arrive with the lesson.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--testing) · [03](#lesson-03--features) · [04](#lesson-04--the-domain-model) · [05](#lesson-05--the-database) · [06](#lesson-06--sign-in) · [07](#lesson-07--file-a-ticket) · [08](#lesson-08--queues) · [09](#lesson-09--take-a-ticket) · [10](#lesson-10--the-tickets-life)

---

## Lesson 01 — Solution setup

**1. Why doesn't `Yasda.Modules.Tickets` reference `Yasda.Shared.Infrastructure`? What could go wrong if it did?**
Infrastructure holds the real implementations: the PostgreSQL connection, the code that reads the user from
the HTTP request. Without the reference, Tickets physically can't reach a connection string or `HttpContext`;
it only sees the small interfaces in BuildingBlocks. With it, a handler could grab the raw request or open
its own connection, and every module would be tied to those details.

**2. You add a sixth shared project, and every module references it. What does that new project boundary
enforce?**
Nothing. A project boundary exists to make something a compile error. If every module may reference it, it
forbids nothing, and it should have been a folder in BuildingBlocks.

**3. How many files change when you upgrade a package the whole solution uses?**
One: `Directory.Packages.props`. Every project takes its version from there.

**4. Why vertical slices instead of `Controllers`, `Services` and `Repositories` folders?**
Because a change is almost always about one feature. With slices, everything for "file a ticket" sits in one
folder, like one drawer holding everything for tacos. With technical folders, one feature is spread over four
places, and shared "services" quietly tie features together.

**5. Why Blazor WebAssembly, and what does it cost on the first visit?**
One language and toolchain for the whole product, C# in the browser talking to our own API, and a server
that only answers API calls instead of holding a live connection per browser. The cost: the first visit
downloads the .NET runtime and the app, a few megabytes, so it takes a second or two to start. After that
it's cached.

**6. Why does the API live under `/api`, and what does `GET /api/typo` answer?**
The app wants page addresses like `/tickets` for itself, and the API wants addresses for data; one prefix
keeps them apart. `GET /api/typo` hits the API group's own fallback and answers 404, never the app's HTML.

**7. Why does YASDA use no component library, and what do we pay for that?**
DESIGN.md's look is plain HTML: if HTML has an element for something, we use that element. A component
library would bring its own designer's look, which we'd spend our time undoing, and a major version that
moves things on every upgrade. Without one, there's nothing to upgrade but .NET, and every page reads in order
with the CSS off. The price: what HTML doesn't have, like a sortable table or a status line, we write
ourselves, once.

**8. `dotnet --version` says "command not found" right after installing it. What's the most likely reason?**
The terminal was opened before the install. A terminal reads the list of program folders (the `PATH`) when it
starts, so it doesn't know about anything installed later. Close it and open a new one.

## Lesson 02 — Testing

**1. What are the three parts of a test, and what does each one do?**
Arrange gets everything ready (a client, a ticket, a user). Act does the one thing being tested. Assert
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

**5. Pick three rows of the edge-case checklist and give an example of each for a ticket.**
For example: *nothing*, a ticket with an empty or whitespace-only subject; *twice*, two agents clicking "Take"
on the same ticket; *who*, a requester opening someone else's ticket (404); *when*, a reply to a ticket that
was closed a moment ago. Any three with a concrete ticket example are right.

**6. What is a regression test? Name the one in this lesson and the bug it guards against.**
Any test rerun after a change to prove nothing broke; in the narrow sense, a test written after a bug so it
can't come back. `The_apps_own_files_are_served` guards against the `ReferenceOutputAssembly="false"`
reference that once left the app without its stylesheet.

**7. Why should most tests be unit tests, and only a few drive a browser?**
Unit tests run in microseconds, never flake, and a failure names one rule. Browser tests need a running app,
take seconds each, and break for reasons unrelated to the rule (timing, layout). Test rules underneath; keep a
few browser tests for the main flows.

**8. A test files a ticket through the API and reads it back, without a browser. Which kind, and what can't it
catch?**
An integration test: the API, the handlers, the rules and the database all run for real, but no screen does.
It can't catch a bug in the page itself: a button that calls the wrong address, a status shown in the wrong
place, a form that never sends the priority. That's what the end-to-end tests are for.

## Lesson 03 — Features

**1. What does the endpoint do, what does the handler do, and what does the validator do? Why are they three
separate pieces?**
The endpoint turns HTTP into a message and the answer back into HTTP. The handler does the work and knows
nothing about HTTP. The validator says what a well-formed request looks like. Apart, the handler is tested
with `new` and a call, the validator is written once and run for every request, and the endpoint stays too
thin to hide a bug.

**2. What is the difference between a command and a query? Give one of each for a ticket.**
A command changes something; a query only reads. `CreateTicketCommand` or "take this ticket" is a command;
`GetTicketQuery` or "list the open tickets" is a query. Any pair like that is right.

**3. Why does the validation filter answer with a `400` instead of throwing an exception?**
A person leaving a box empty is an everyday event, not an exceptional one. An exception is slower, fills the
logs with noise, and the filter can answer the request directly, with the field names a form needs.

**4. Why do we use FluentValidation and not .NET 10's built-in validation?**
The built-in one only checks public classes, and our request types are `internal`. It skips them silently
and lets bad data through with a 200.

**5. You add a validator and it never seems to run, and nothing reports an error. What are the two most likely
causes?**
`AddValidatorsFromAssembly` was called without `includeInternalTypes: true`, so it skipped the internal
validator; or the `/api` group lacks `.ValidateRequests()`, so no filter runs validators at all.

**6. Why does a unit test of the validator not replace the integration test that sends an empty message?**
The unit test proves the rule is right. It can't prove the rule is *used*: that depends on module
registration and on the filter, and forgetting either leaves the unit test green while the API accepts bad
input.

**7. What does `AddScoped<PingHandler>()` do, and how do you find out you forgot one?**
It registers the handler so each HTTP request gets its own instance, injected into the endpoint. If it's
missing, the endpoint can't be built; `Every_feature_handler_is_registered` names the unregistered handler.

**8. Why may only test projects get `InternalsVisibleTo`, and what test makes sure of it?**
Any other friend (the host, another module) can then use the module's internals, which turns two islands into
one. `Internals_are_shared_only_with_tests` reads the attributes out of each module and fails on any name that
doesn't end in `Tests`.

## Lesson 04 — The domain model

**1. Why are `Ticket`'s setters private, and what test makes sure of it?**
So the only way to change a ticket is through its methods, which check the rules. A public setter would let
any caller skip them. `Domain_state_changes_only_through_its_methods` fails on any public setter in a
`.Domain` namespace.

**2. What does `Reply` do differently for a requester's answer and an agent's reply?**
A requester's answer takes a Pending ticket back to Open and reopens a Resolved one (within 7 days); a
requester can't write internal notes. An agent's public reply records the first-response time and takes a
ticket nobody has; an internal note does neither, because the requester never sees it.

**3. Why does `Ticket.Create` take the time as a parameter instead of reading the clock?**
So a test chooses exactly when things happen, with no waiting and no flaky edges. The window of 7 days is
tested to the tick because the test controls "now".

**4. What's the difference between a validator's refusal and a `DomainException`? Give a ticket example of
each.**
A validator says the request is malformed (an empty subject, an unknown priority) and answers 400 before
anything runs. A `DomainException` says a well-formed request breaks a business rule (taking a ticket Alan
already has, reopening after 8 days) and carries a `TICKETS-` code. Malformed versus not allowed.

**5. Why do ids have their own types (`TicketId`, `UserId`) and not plain `long`?**
With plain `long`s the compiler accepts a ticket's number where a person's belongs. With two types, mixing
them up doesn't compile.

**6. What does a property-based test do that an ordinary test doesn't? What did ours find?**
It states a rule that must always hold and lets FsCheck try hundreds of generated cases, including ones nobody
thought of. Ours found that an agent's reply to a New ticket opened it with nobody responsible; the fix is that
replying to an unassigned ticket takes it.

**7. A teammate wants to reuse `TICKETS-0007` for a new rule because the old one is no longer used. What do you
tell them?**
No. A published code never changes meaning and its number is never reused: someone may have written it in a
message or a log. The new rule gets the next number at the end of the list.

**8. Why must the domain not reference ASP.NET Core or EF Core, and what test says so?**
Plain C# with no database or web server is what makes every rule testable in milliseconds, and keeps the rules
from depending on how data is stored or served. `The_domain_does_not_depend_on_data_access_or_web` fails if a
domain class names such a type.

## Lesson 05 — The database

**1. Why does each module get its own schema and its own migration history?**
So a module owns its tables the way it owns its code: nobody else reads or changes them, and each module can
migrate, be tested and later be moved on its own. A shared history would tie every module's releases together.

**2. What is change tracking, and what does it cost?**
EF Core remembers every object it loaded, and on save writes only what changed. The cost is memory and time for
all that remembering, fine for writing one ticket at a time, too heavy for long lists, which is why reads move
to Dapper in lesson 08.

**3. Why do we store a ticket's status by name instead of by number?**
The table is readable in plain SQL (`'Open'`), and reordering or inserting into the C# enum later can't
silently change what old rows mean.

**4. A teammate edits `InitialCreate` to add a column. Why is that a problem, and what should they do instead?**
Any database that already ran `InitialCreate` never runs it again, so it never gets the column, and the
schemas drift apart. A shipped migration is never edited: change the map and add a new migration.

**5. What makes the seed data idempotent, and why does the app depend on that?**
It first checks whether any tickets exist and does nothing if so. The app seeds on every development startup, so
without the check each restart would add three more tickets.

**6. Why does `A_ticket_and_its_conversation_survive_a_round_trip` read in a second scope?**
A new scope is a new `DbContext` with no memory. In the same scope EF Core would hand back the very object it
just saved, and the test would pass without the database being involved at all.

**7. Why does the connection string for production come from the server's settings, not from
`appsettings.json`?**
It's a secret and it differs per installation; `appsettings.json` is committed with the code. Configuration
describes the installation, and the code doesn't.

**8. What does the check constraint on `status` protect against that the domain doesn't?**
Anything that bypasses the domain: a hand-written SQL statement, a future bug or another tool writing to the
table. The database refuses a status it doesn't know whatever wrote it.

## Lesson 06 — Sign-in

**1. Why do we keep a browser app's sign-in in an `HttpOnly` cookie instead of a token in the page's own code?**
Anything the page's code can read, a script injected into the page can read too, and anything shipped to a browser
is public. An `HttpOnly` cookie can't be read by the page's code at all; the browser just attaches it to each call.
The app learns who you are by asking `/api/auth/me`.

**2. What does lockout protect against, and what does the user experience after the fifth wrong password?**
Guessing: someone trying thousands of passwords on one account. After the fifth wrong one, the account is locked
for 15 minutes and even the right password is refused until then.

**3. Why does the API answer 401 instead of redirecting to the sign-in page?**
A client of an API (the browser app, a test, a script) wants a status it can act on. A redirect to an HTML page
would be followed silently and answer 200 with the wrong content.

**4. Why is `/auth/register` switched off, and what test proves it?**
Accounts are created by an administrator; a service desk where strangers can make accounts isn't one.
`Self_registration_is_disabled` expects a 404.

**5. What is an open redirect, and which line of `Login.razor` prevents one?**
A sign-in page that sends the person wherever `returnUrl` says, including another site, so a crafted link can
bounce a freshly signed-in person to a fake page. `stayInsideTheApp` accepts only addresses with no `//` and no `:`.

**6. Why does `_Imports.razor` carry `@attribute [Authorize]`? What happens to a new page you forget to think about?**
It makes "needs a signed-in user" the default for every page. A forgotten page is protected, not public; a public
page has to say `[AllowAnonymous]` on purpose.

**7. Why must `App.razor` and the page server be `AllowAnonymous`?**
They send the page that contains the router, and the router is what sends a signed-out person to the sign-in page.
If they asked for a sign-in, nobody could ever reach the sign-in page.

**8. Why does a browser test find a button by its role and name instead of its CSS class, and what does
`Expect(...)` do that reading a value once doesn't?**
Role and name is how a person or screen reader finds it, so a control that can't be found that way has an
accessibility bug; a CSS class can change without anyone noticing. `Expect` retries until the page shows the thing
or time runs out; reading once races the page and fails at random.

## Lesson 07 — File a ticket

**1. Why does Tickets copy the requester's name onto the ticket instead of reading it from Identity each time?**
A list of hundreds of tickets must show names without asking Identity for each one, and without a join across
schemas, which the module rules forbid. The cost: a person who is renamed keeps their old name on old tickets.

**2. What is in a Contracts project, and what may it reference? Why that last part?**
Interfaces, their data types and (later) integration events: the module's service window. It may reference only
`Yasda.Shared.BuildingBlocks`, so that two modules calling each other can never create a cycle of project
references.

**3. Which two kinds of failure are answered *without* an exception, and which kind is thrown as a
`DomainException`?**
A malformed request (the validation filter answers 400) and a missing record (the endpoint answers 404) need no
exception: they're everyday events. A well-formed request that breaks a business rule throws a `DomainException`.

**4. What does the exception handler answer for a `DomainException`, and where does the code in the answer come from?**
`422 Unprocessable Entity`, with the rule's own code, a link `/errors/{code}`, its title, the exception's message as
the detail, and a trace id. The code comes from the module's error catalog (`TicketsErrors`), through the exception.

**5. Why must the error catalog's endpoints be open to a signed-out visitor?**
The link inside an error message has to work even when the error is "your session ended". If it asked you to
sign in, the explanation would be locked behind the problem it explains.

**6. What does `Every_code_is_used_somewhere` protect the people who read the catalog from?**
Looking up an error that can't happen. A code nobody throws is a promise nobody keeps, so the test fails on one.

**7. Why does `ApiClient` disable the Send button while a change is on its way?**
`Busy` is true until the answer arrives, so a double click can't send the same ticket twice.

**8. A test needs to prove the architecture test for module references can fail. Why isn't adding a project reference enough?**
The compiler leaves a reference out of the compiled module when no code uses it, and the test reads the compiled
module. The code must use a type from the other module, such as `typeof(IdentityModule)`.

## Lesson 08 — Queues

**1. Why do reads use Dapper while writes use EF Core? Give one cost of EF Core that a list doesn't need.**
Writes need the domain's rules and change tracking, which EF Core gives. A list only needs rows shown fast. EF Core
builds a full object for every row and tracks it in memory in case it changes, which a list never does.

**2. What is SQL injection, and which two things in this lesson stop a `?sort=` from becoming one?**
Letting request text become part of a SQL statement, so whoever controls the text controls the database. The validator's
allow-list refuses any sort not on the list, and the SQL function picks the order with `CASE` from fixed names, so it never
builds an `ORDER BY` from the text.

**3. How does a requester end up seeing only their own tickets, and where is that rule written?**
The handler passes the requester's id to the SQL function, which filters on it. For staff the id is null, which means all.
The rule is in the handler's decision and the function's `WHERE` line, and tests prove it for the list and the page.

**4. Why does the API answer `404`, not `403`, for someone else's ticket?**
A 403 says the ticket exists but you may not see it. A 404 tells a stranger nothing, not even that ticket #7 exists.

**5. Why must `ValidSort` list the names *exactly*, and what would it let through if it ignored case?**
The names are the columns' JSON names, and the SQL matches them exactly. Ignoring case would let `Subject` through to
a `CASE` that doesn't know it, so the list would quietly use the default order instead of saying the request was wrong.

**6. Why does the queue keep its filters, sort and page in the address instead of in the page's fields?**
Back and Forward work, a bookmark or pasted link shows the same list, and a sort or page is just a link, with no state
to lose on reload.

**7. A teammate wants to add a "team" filter to `search_tickets`. What do they create, and what do they leave alone?**
A `tickets.search_tickets.v2.sql` with the new parameter, and a new migration that runs it. They leave the released
`.v1.sql` and its migration untouched.

**8. Why does `search_tickets` end its `ORDER BY` with `t.id DESC`?**
Two tickets can share an update time. Without a tie-breaker, the database may order them differently from one page to
the next, so a ticket could appear on two pages or on none.

## Lesson 09 — Take a ticket

**1. Why isn't "check that nobody has it, then save" enough to stop two agents taking the same ticket?**
The check and the save are two moments. Both agents can load the ticket while nobody has it, both checks pass, and the
second save quietly writes over the first. What was checked is out of date by the time of the save.

**2. What does the row version do on a save, and what does EF Core throw when it doesn't match?**
The save says "update this row only if its version is still the one I loaded". If someone changed the row in between, no
row matches, nothing is written, and EF Core throws `DbUpdateConcurrencyException`, which the API answers with `409`.

**3. Name the two ways the second agent can lose. For each, who stops them, and what do they get back?**
They loaded the ticket before the first agent saved: the row version stops them, and they get `409` with `YASDA-0003`.
They loaded it after: `Ticket.Take` sees someone has it and throws, and they get `422` with `TICKETS-0007`.

**4. We added no column for `xmin`, and the migration ran no SQL. Why?**
Every PostgreSQL row already has `xmin`, and Npgsql knows it is a system column, so it leaves the `ALTER TABLE` out. The
migration only records itself and updates the snapshot, so EF Core's pending-changes check stays quiet.

**5. Why does the race test run ten rounds and accept either of two losing answers? Why do we also have the stale-copy test?**
One lucky ordering proves nothing, and which losing answer comes back depends on timing, so the test insists only on what
must always hold: exactly one `204`. The stale-copy test arranges the order itself, so it fails every time the row version
is missing, instead of only sometimes.

**6. Tickets needs to know whether a person is staff. Why does it call `FindStaffAsync` instead of reading the users table?**
Who is staff is Identity's knowledge, and a module may not read another module's tables. The contract is the one service
window, and Identity can change how it answers without Tickets noticing.

**7. A request assigns a ticket to person `-5`, another to person `9999` who is a requester, another to ticket `0`. What status does each get, and what produces it?**
`-5` is `400`, from the validator, before the handler. A requester's id is `422` with `TICKETS-0011`, from the handler after asking Identity.
Ticket `0` doesn't exist, so the handler returns `false` and the endpoint answers `404`.

**8. Why is the role check on the endpoint, but "is the ticket taken?" in the domain?**
"Only role X may call this" doesn't depend on any data, so the endpoint's door answers it before the handler runs.
"Is it taken?" depends on the ticket's own state, so it is a rule of the ticket, in one place.

## Lesson 10 — The ticket's life

**1. Why is there one `ChangeTicketStatusHandler` for five endpoints, and what is the cost of that choice?**
The five handlers would be the same except for one line, so a bug fixed in one would be forgotten in the other four. The
cost is a `switch` with five branches: a new step means a new branch. It stays honest as long as every branch only
dispatches, and every rule stays in `Ticket`.

**2. A requester calls `/resolve` on their own ticket, and another requester cancels it. What does each get, and which door answered?**
The first gets `403`, from the endpoint's role door, before the handler runs. The second gets `404`, from the handler's
load: `GetVisibleTicketAsync` returns `null` for a ticket that isn't theirs, so the endpoint can't tell it from a missing one.

**3. Why does `GetVisibleTicketAsync` return `null` for both "no such ticket" and "not yours"?**
A `403` would tell a stranger that the ticket exists. A `404` tells them nothing, not even that ticket #7 is real.

**4. Lesson 09's handlers loaded a ticket by id. What changed, and why did `userIsStaff: true` stay safe there?**
They now load through `GetVisibleTicketAsync`, the one shared way. `userIsStaff: true` is safe because the endpoint's role door
only lets staff reach those handlers, so the handler never sees a requester.

**5. A resolved ticket is eight days old, and its requester clicks Reopen. What happens, and which code does the person see?**
`Ticket.Reopen` finds the 7-day window passed and throws, so the API answers `422` with `TICKETS-0009`. The status line
shows the message and a link to the code's page, and the ticket stays resolved.

**6. Why does the cancel button ask first, and why inline instead of in a pop-up?**
Cancelling can't be undone, so the person should read what it does before it happens. Inline, the question sits on the page
with buttons that say what they do, and it doesn't steal focus or break the back button like a pop-up dialog.

**7. What does `Fail<T>` do for `Get`, `Post` and `Create`, and what did each of them do before?**
It shows the error and answers "nothing" in the caller's own type: `null` for `Get` and `Create`, `false` for `Post`. Before, `Fail`
answered nothing and every caller wrote its own `return default`, with the busy-counting lines repeated in each method.

**8. Nothing closes a resolved ticket after seven days. Why is that honest, and when will it change?**
The reopen window is enforced (a late reopen is refused), but the ticket stays `Resolved` until an agent closes it, and the
lesson says so. Closing by itself needs a scheduler and a setting an admin turns on, which arrive in a later milestone.

---

## Lesson 11 — Replies and notes

**1. Why do a reply and an internal note share one handler and one endpoint? Where is the difference decided?**
They are the same act with one difference: who may read the message. The request carries an `isInternal` flag, and
`Ticket.Reply` decides what it means: only staff may write a note, and a note isn't a response to the requester.

**2. A requester sends `{ "body": "Psst", "isInternal": true }`. What do they get back, and which layer said no?**
A `422` with `TICKETS-0008`. The domain said no: `Ticket.Reply` knows the author isn't staff. The validator can't know,
because the body is well-formed.

**3. Why did lesson 09's version check not cover replies by itself? Which one line makes it cover them?**
A reply only inserts a message row, so without another change there is no `UPDATE` of the ticket and no version check.
`UpdatedUtc = nowUtc;` at the end of `Ticket.Reply` changes the ticket's row, so the save checks `xmin`.

**4. What does the stale-copy test use an internal note for, instead of an agent's public reply?**
A public reply from an agent also takes the ticket and opens it, which changes the row for other reasons and would hide the
missing `UpdatedUtc`. A note changes nothing on the ticket except `UpdatedUtc`, so it tests exactly that line.

**5. Two agents reply to the same ticket at the same moment. What does the slower one see, and what is the honest cost of that?**
A `409`, "Someone else changed this first", and the page reloads showing the other reply. The cost is that the second agent must
send theirs again. It's the price of never answering a ticket whose state you didn't see.

**6. Why is the priority change a `PUT` and not a `POST`, and why is `userIsStaff: true` safe in its handler?**
It replaces one field of something that exists, where a `POST` starts an action. `userIsStaff: true` is safe because the
endpoint's role door lets only staff in.

**7. Why does `Reply()` in the page keep the text when the send fails, and reload the ticket either way?**
The person typed a paragraph, and a `409` or a lost connection mustn't eat it. The reload shows the ticket as it is now,
including whatever the other person just wrote.

**8. Why can't staff edit a message or a subject?**
They are what people said. If they could be rewritten, nobody could trust the history of a ticket.

---

## Lesson 12 — Containers and CI

**1. Name three problems the shared `yasda_test` database caused.**
Yesterday's rows were still there, so a test could pass only because of leftovers. Two runs at once changed the same tables
and failed for no reason in the code. And every machine needed the same PostgreSQL, installed and set up the same way, which a
fresh machine, such as a build server, doesn't have.

**2. What is the difference between an image and a container? And a volume?**
An image is the frozen program with everything it needs, one file. A container is a running copy of it, isolated from the
machine. A volume is storage that lives outside the container, so the data survives when the container is removed.

**3. Give two reasons the course uses Podman. Would anything change if your company uses Docker?**
It's free for any use under the Apache license, and it runs rootless with no background service. Nothing changes with Docker:
both use the same OCI images, and Testcontainers talks to whichever it finds.

**4. Why did no test change when the factory moved to Testcontainers?**
Every test reached the database through `factory.OpenDatabase()`, so the factory was the only place that knew where the
database was.

**5. What does Ryuk do, and when does it matter?**
It's Testcontainers' cleaner: a small container that removes the test containers when the test run ends. It matters when the
run crashes or is stopped halfway, because then nothing else would remove them.

**6. A test run reports 95 percent coverage. What has that told you, and what hasn't it?**
That 95 percent of the lines ran during the tests. It hasn't said whether any test checked what those lines did: a line can run
without anything asserting on it.

**7. What is a surviving mutant, and what should you do about one?**
A small change Stryker made to the code that no test noticed: every test still passed. It's a bug your tests would miss. Either
write the test that kills it, or decide the change doesn't matter.

**8. Why does the coverage step say `if: always()`, and why doesn't the mutation job run on every push?**
So the report is made even when a test failed, which is when it helps most. The mutation run runs the tests once per mutant,
which is slow, so it runs only when someone presses **Run workflow**.

---

## Lesson 13 — Events and the outbox

**1. Why is calling Sla from `CreateTicketHandler` unsafe even if Tickets were allowed to reference it?**
It is two separate writes with no transaction around both. A crash between them saves a ticket with no clock; swapping the order gives a clock for a ticket that doesn't exist. That is the dual write problem.

**2. What does the outbox write in the same transaction as the ticket, and what does that guarantee?**
The ticket's events, as rows in the module's `outbox_message` table. Either the ticket and its events are both saved, or neither is, so an event can't be lost or invented.

**3. What is the difference between a domain event and an integration event? Which one lives in a `.Contracts` project?**
A domain event is in the module's own words and `internal`; only its own module hears it. An integration event is plain data, `public`, and other modules hear it. The integration event lives in `.Contracts`.

**4. The outbox delivers at least once. What must every handler therefore be, and how does `StartClockOnTicketCreated` achieve it?**
Idempotent: hearing an event twice must leave the same result as once. It checks whether the clock exists first, and treats a unique-key violation (a racing delivery got there first) as success.

**5. Why did a new ticket's event say "ticket #0" before we added the sequence?**
The interceptor writes the event before PostgreSQL hands out the identity value, so the new ticket still had number 0.

**6. Why does the migration drop the identity before it creates `ticket_id_seq`, and why does it call `setval`?**
An identity column owns a sequence named `ticket_id_seq`, the very name we want; dropping the identity removes it. `setval` starts our sequence above the highest existing ticket so the next ticket doesn't reuse a number.

**7. Why does `SlaClock` compute due times from the event's time and not from "now"?**
A delivery can arrive hours late. A deadline worked out from "now" would move; from the event's own time it never does.

**8. Why does `StartClockOnTicketCreated` catch a unique-key violation instead of letting it fail?**
Two deliveries of one message can race; both see "no clock yet", one wins. The loser's failure means the clock exists, which is the answer wanted, so it must count as success.

**9. What does `FOR UPDATE SKIP LOCKED` let two copies of the app do?**
Each takes different messages: rows one dispatcher has locked are skipped by the other, so nothing is delivered twice at the same moment.

**10. What happens to a message whose handler fails ten times, and why is that better than deleting it?**
It is parked: left in the table with its error. A parked message is a fact a person needs to look at; deleting it would hide that something that should have happened did not.

**11. A ticket is resolved with no reply and shows "Breached". Is that a bug? Why?**
No, it is a decision. Only an agent's public reply stops the response target, so a ticket fixed without ever answering keeps showing its unanswered target. The cost is that a quickly fixed ticket can still look breached.

---

## Lesson 14 — Sla answers, the screens show it

**1. Why does Tickets keep a copy of the deadlines instead of asking Sla for each row?**
A page of twenty rows would be twenty calls into another module, and the queue could not sort by deadline in one SQL query, since no SQL may cross a schema.

**2. What does "eventual consistency" mean for the queue, and why is it fine here?**
The copy catches up a moment after Sla's answer, so for a second or two the queue can show the old deadline. Deadlines are measured in hours, so nobody can tell.

**3. Why is `ticket_sla` its own table, and not columns on `ticket`?**
Saving deadlines must not change the ticket's row, or its `xmin` row version would change and an agent's save would fail for no visible reason. A separate table also raises no ticket event.

**4. What does `TicketSla.Apply` do with a version it has already seen, and with an older one?**
Nothing: `if (version <= Version) return false;`. Only a newer version is taken, and the caller learns whether it was.

**5. Why does the v2 migration drop the old functions before creating the new ones?**
A function can't change what it returns, so `CREATE OR REPLACE` would fail. The old ones are dropped, and the v2 files create them again.

**6. Where is a requester's deadline removed, and which test proves it?**
In the handlers: `SearchTicketsHandler` empties the two columns and `GetTicketHandler` leaves the whole `Sla` null for anyone who isn't staff. `A_requester_never_receives_SLA_fields` proves it at the API.

**7. Why does the browser compare two times with "now" instead of asking the server how long is left?**
The server decides what "at risk" means and sends two times. The browser compares them with its own clock, so "1h 10m left" stays true as minutes pass without any request.

**8. Why does the architecture test for integration events wait until this lesson?**
It fails for an integration event no other module handles. Until Tickets listened to `DeadlinesChanged` it would have failed; now it can pass.

---

## Lesson 15 — Observability

**1. What are the three things a trace, a metric and a log each tell you?**
A log says what happened to one thing, a metric says how much of something there is over time, and a trace shows the path of one request as a tree of timed spans.

**2. Why must the liveness check never look at the database?**
A database outage would then make the platform restart a healthy app, again and again, fixing nothing. Liveness only says the process is running.

**3. Why is a message that was given up on "degraded" and not "unhealthy"?**
The app still serves requests; only something that should have happened didn't. Unhealthy would trigger a restart that fixes nothing. Degraded says a person is needed.

**4. What does `traceparent` hold, and where is it written and read?**
The trace id and span id of the running request, in a 55-character W3C string. `OutboxMessage.From` writes it into the row; the dispatcher reads it and uses it as the parent of the delivery span.

**5. Why is `trace_parent` a nullable column?**
Rows already in the table have none, and a message written when nothing was being traced has none either.

**6. What is the difference between a counter and a gauge? Which does the backlog use?**
A counter only goes up, so a tool shows its rate. A gauge goes up and down and is read when metrics are collected. The backlog (pending, parked) uses gauges.

**7. Why is `/health` left out of the traces?**
A probe every few seconds would bury the real requests.

**8. Why is the dashboard its own project, and what does the app know about where telemetry goes?**
It is development only and must never be deployed. The app knows one setting, `OTEL_EXPORTER_OTLP_ENDPOINT`, and nothing about any vendor.

---

## Lesson 16 — Users and the sign-in log

**1. Why do we never write to Identity's tables with our own SQL?**
We would skip what Identity does for us: hashing the password, checking its strength, keeping the lockout count and the security stamp. We use `UserManager` to write, and our SQL functions only to read.

**2. Which rules live in `StaffAccountRules`, and why in the domain?**
Nobody disables their own account, the last active admin stays, nobody changes their own role, and a role must exist. They are business rules ("is this allowed?"), so they are plain functions that throw a `DomainException` with a code, testable without a database.

**3. What does a new security stamp do, and when do we change it?**
It makes every cookie and token the person holds stop working at its next check. We change it when a role changes, when an account is disabled, and when someone signs out everywhere.

**4. Why is a failed sign-in for an unknown email recorded as `InvalidCredentials`?**
So neither the log nor the API's answer reveals which emails exist: a wrong guess at a real account and at a made-up one look the same.

**5. Why does the first admin come from configuration, and why does it do nothing once users exist?**
A fresh install has no users and self sign-up is blocked, so nobody could create the first account. Once users exist it is ignored, so a setting someone forgot to remove can't recreate an account later.

**6. What are the two documented hooks we use on `SignInManager`?**
`CanSignInAsync`, where a disabled account is refused, and `PasswordSignInAsync`, where every attempt is recorded. Hashing, lockout and tokens stay Identity's.

**7. Why does the change-password handler sign the person in again?**
Changing the password changes the security stamp, which would end the current session too. Signing in again re-issues the cookie.

**8. Why is `SmtpEmailSender` allowed to log the message instead of sending it?**
With no SMTP host configured (development), logging lets the forgot-password flow be followed without a mail server. In production the host is set and it sends.

---

## Lesson 17 — Teams

**1. Why do teams live in Identity, and what will Tickets keep of one?**
A team is a set of people, and people belong to Identity. Tickets keeps only the team's number and a copy of its name, with no foreign key across schemas.

**2. What are the three notify modes, and who is told in each?**
Team leads only (the leads); team leads and selected members (the leads plus the ticked members); everyone (every member). Only active accounts are told.

**3. Who hears about a ticket that has no team?**
Every active team lead.

**4. Why can't a team's members change without going through the team?**
Adding one has a rule (only staff), and the team's own row must change when its members do, or its row version would never be checked and a stale save would slip through.

**5. Why is "is this name taken?" answered outside the `Team` and passed in?**
It needs the database, and the domain must stay free of data access. The caller finds out and passes `nameTaken`; the team decides what to do about it.

**6. Why does `team_member.user_id` have no foreign key, and what is the cost?**
The ids have different types, and the island rule keeps later modules from joining across schemas. The cost: the database wouldn't stop a member row pointing at a deleted user. Accounts are only ever disabled, and disabled members are never told.

**7. Why is the notify list one query behind `ITeamDirectory` and not copied where it is needed?**
Two copies drift apart. Identity owns the data, so Identity answers, and everyone asks the same question the same way.

**8. Why does changing `list_staff`'s arguments need a `.v2` file and a migration that drops the old function?**
A released script is never edited, and a function with different arguments is a different function: the old one would stay beside the new one unless the migration drops it.

---

## Lesson 18 — Categories and routing

**1. Why does a ticket carry a copy of its team's name, and not ask Identity each time?**
One call per row of the queue would be slow, and the queue could not filter by team in one SQL query. Tickets keeps the team's number and a copy of its name, and an event keeps the copy up to date.

**2. Why is "is this person in the team?" asked in the handler and passed to the ticket?**
The domain must stay free of calls to other modules. The handler asks Identity and passes `isMember`; the ticket decides what the rule says, so it can be unit tested with no database.

**3. Why does `Reply` need the same membership guard as `Take`?**
An agent's public reply to an unowned ticket takes it. Without the guard, an agent outside the team could take a team's ticket by replying, a back door round `Take`.

**4. What happens to a ticket's owner when a team lead routes it to a team they are not in? To its status?**
The ticket loses its owner. An Open or Pending ticket goes back to New, because a ticket in progress always has an owner.

**5. Why do `Take` and `Assign` default their `isMember` flag to `false`?**
The safe side: forgetting to pass it can only refuse someone, never let the wrong person in.

**6. Why does `RenameTeamCopies` ask Identity for the current name?**
Two quick renames can be delivered in either order, so the name in the event may already be old. The current name is the truth.

**7. What makes `RenameTeamCopies` safe to run twice?**
It only updates rows whose copy differs from the current name, so a repeat finds nothing to change.

**8. Why does `BigintArrayParameter` exist?**
Dapper turns a list parameter into `(@a1, @a2)`, which suits an `IN` clause but breaks a function argument. The class passes the list as one real PostgreSQL `bigint[]`.

---

## Lesson 19 — Email

**1. Why must a handler in the post office only queue mail, and not send it?**
The dispatcher retries all of a message's handlers if one fails. A mail server that is down would make the whole delivery fail, so unrelated handlers, such as Sla's clock, would never run, and the message would end up parked.

**2. What makes queueing the same mail twice harmless?**
Each mail has a unique key, `"kind:eventId:userId"`. Queueing an existing key does nothing, and a race between two deliveries is settled by the unique key: the loser counts as success.

**3. Why does Tickets never see an email address?**
It calls `IMailQueue.QueueAsync` with a user number. Identity looks up the address and skips a disabled or unknown account, so the address stays in the module that owns it.

**4. What is `EventId` for?**
It names one happening. It becomes part of each mail's key, so a redelivered event queues each mail once.

**5. Who gets mail when a requester replies to a ticket nobody has?**
The team's notify list (the leads, plus whoever the notify mode adds), or every active team lead when the ticket has no team.

**6. Why does an assignment to yourself send nothing?**
It isn't news to you. `Take` passes the agent as both the assignee and the assigner, and the handler skips that case.

**7. What is the one gap the mail queue can't close, and why?**
A crash between the mail server accepting a mail and the row being marked sent sends it twice after the restart. There is no transaction across the mail server and our database.

**8. Why is `IMailer` a singleton?**
The transient `IEmailSender` depends on it, and a transient service can't depend on a scoped one. The mailer holds no state, so a singleton is fine.


## Lesson 20 — The knowledge base, part one: articles and the review

**1. Why does an article keep a working copy and numbered versions, and not one body and a status?**
Editing and reading need different things. Editors need a place to change words freely; readers need text that never changes under them. A published article's working copy can be rewritten while readers keep reading the last version, and every version records who published it and when.

**2. Who can't publish an article, and what is the exception?**
The person who last changed the text. An Admin is the exception, so an installation with a single person in it can still publish. The trail records who did it either way.

**3. Why does every method of `Article` set `UpdatedUtc`, even one that only adds a history row?**
The article's row carries PostgreSQL's `xmin` as its version. A change that only adds a row to a list leaves the article's own row untouched, so its version doesn't move, and two people changing it at once would both succeed. Touching the article makes the second one fail.

**4. Why are `MayEdit` and `MayManageEditors` static?**
So a query, which can't load the article, can ask the same question the article asks, on the data it just read, to decide which controls to show. One rule, written once.

**5. Who may edit an article, and which of them is decided by another module?**
The author, someone on its edit list, a KbEditor or Admin, and a team lead of the author's team. The last is a fact about teams, so Identity answers it (`LeadsTeamOfAsync`) and the handler passes the answer in.

**6. Why does `Publish` compare the publisher with `LastEditedById` and not with the author?**
The rule is about the words, not the job. If a KbEditor rewrites someone's draft, the KbEditor wrote what is now in review, so someone else has to approve it. Comparing with the author would let the rewriter approve their own rewrite.

**7. Why does a module that raises no events still have an `outbox_message` table?**
The outbox dispatcher, its backlog gauges and its health check look for that table in every module's schema. Without it, they would log an error on every tick.

**8. Why does the article write its own history, and not an event handler?**
An event would need a handler to justify it, and a handler runs after the commit, so a crash could lose the line. The article adds the history row in the same save as the change, so the trail can't be forgotten and can't disagree with what happened.

