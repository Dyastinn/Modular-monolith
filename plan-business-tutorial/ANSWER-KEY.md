# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

The course grows with the product, so this page grows with it: each lesson's answers arrive with the lesson.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--testing) · [03](#lesson-03--features) · [04](#lesson-04--the-domain-model) · [05](#lesson-05--the-database) · [06](#lesson-06--sign-in) · [07](#lesson-07--file-a-ticket) · [08](#lesson-08--queues)

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
