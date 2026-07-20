# Answer Key — Every "Check Yourself" Question

> 👨‍🏫 **From your senior:** these are *model* answers, not the only right ones. If you said the same thing in
> different words, you're fine — what matters is whether you named the **reason**, not whether you matched my
> phrasing. If your answer was right but for a different reason than mine, that's worth a second look: in this
> course the reason *is* the lesson.

**Use this properly, or it'll cost you.** Attempting an answer and getting it wrong plants the idea far
deeper than reading a correct one does — that's not a motivational slogan, it's the single most replicated
result in learning research. So: try the question first, out loud or on paper, *then* come here. If you look
first, you'll get the warm feeling of understanding without the understanding.

> 📋 **Delivering this as training?** These double as an instructor's discussion guide. The questions marked
> 🎯 are the ones worth doing as a group — they're where learners most often hold a plausible-but-wrong model,
> and hearing someone else's wrong answer is usually what fixes it.

**Jump to:** [1](#phase-1-solution-setup) · [2](#phase-2-mediator) · [3](#phase-3-database) ·
[3b](#phase-3b-data-access) · [4](#phase-4-passwords-and-access-tokens) · [4e](#phase-4e-auth-wrap-up) ·
[5](#phase-5-domain-model) · [6](#phase-6-create-sales-order) · [7](#phase-7-submit-and-concurrency) ·
[8](#phase-8-approval-and-cancellation) · [9](#phase-9-update-order) · [10](#phase-10-queries-and-search) ·
[11](#phase-11-cross-cutting-concerns) · [12](#phase-12-testing) · [13](#phase-13-capstone) ·
[14](#phase-14-going-further) · [14b](#phase-14b-deploying-to-iis) · [15](#phase-15-scaling-the-wall)

---

## Phase 1: Solution Setup

**1. Why is the solution split by module + Shared kernel instead of four Clean Architecture layers?**

Because the four-layer split (`Domain`/`Application`/`Infrastructure`/`Api`) organizes by *technical kind*,
which scatters every single feature across four projects: "create a sales order" lives in four places, and
you touch all four to change one thing. Splitting by **module** (a business capability) and then by
**feature** keeps everything about one job in one place. You still get Clean Architecture's real prize — a
pure, testable domain — from the second rule ("keep the Domain pure"), without the ceremony. The test: in
this layout, "where is the create-order code?" has one answer, not four.

**2. What compiler-level thing stops `Sales` from reaching into `Inventory`?**

The **absent project reference**. `Erp.Modules.Sales.csproj` has no `<ProjectReference>` to
`Erp.Modules.Inventory`, so as far as the Sales compiler is concerned, Inventory's types do not exist —
`using Erp.Modules.Inventory;` won't compile. The wall isn't a convention or a code-review rule; it's the
build failing. *(The optional [Phase 15](15-scaling-the-wall.md) trades this for a different compiler-enforced
wall: a reference plus `internal`.)*

**3. 🎯 `IDbConnectionFactory` lives in `Shared.BuildingBlocks`, `SqlConnectionFactory` in `Shared.Infrastructure`.
Why different projects, and which may `Sales` see?**

`Sales` may see **`IDbConnectionFactory`** (the interface) and *not* `SqlConnectionFactory`. They're split so
the split can be *enforced*: `Shared.BuildingBlocks` depends on nothing, so any module can reference it safely;
`Shared.Infrastructure` holds everything that touches SQL Server, JWTs, and `HttpContext`, and **only the host
references it**. Because that reference doesn't exist from `Sales`, a module physically cannot reach a
connection string, a signing key, or `HttpContext` — those types aren't on its compile path. Interfaces down
low where everyone can depend on them; implementations up high where only the composition root sees them.

**4. Why does this phase install only Swashbuckle and no other packages?**

Because it's the only package this phase's code actually uses. The rule is **install a package in the phase
that first needs it** — install everything up front and you get a `.csproj` full of dependencies nobody can
justify, and no reader can tell which package is load-bearing for what. (It's the same discipline as adding
config only when needed, and — from Phase 4b on — adding an exception-handler arm only when a phase first
throws that type.)

**5. What does Central Package Management give you, and where do versions live once it's on?**

It gives you **one version per package for the whole solution**, so eight projects can't drift onto three
versions of Dapper. Versions live in a single `Directory.Packages.props` at the solution root
(`<PackageVersion Include="…" Version="…" />`); each `.csproj` then references the package with **no**
`Version` attribute. Upgrading is a one-line edit in one file instead of a hunt through ten.

---

## Phase 2: Mediator

**1. Name two concrete differences between the hand-rolled mediator and the free library.**

Any two of: (a) **when you find out a handler is missing** — hand-rolled fails at *runtime* with "no service
for `IRequestHandler<…>`", the library warns at *compile time*; (b) **how dispatch works** — ours uses cached
reflection, the library source-generates the dispatch (faster, no reflection); (c) **return type** — the
library's handlers return `ValueTask<T>`, ours return `Task<T>`; (d) **whose bug it is** — a defect in ours is
yours to fix, a defect in theirs is a dependency you wait on.

**2. Why can the rest of the tutorial ignore which option you chose?**

Because everything *uses* the mediator through the same interfaces — endpoints only ever call
`sender.Send(...)`. The call sites don't name an engine, so swapping engines doesn't touch them. Be precise
about the limit, though: your **handler and behavior signatures** do follow whichever engine you picked
(`Task` vs `ValueTask`, whose `IPipelineBehavior`). "Swap the engine, keep the code" is true for endpoints and
orchestration, not for every line.

**3. 🎯 What do the `ICommand`/`IQuery` markers buy us if they contain no code?**

Two things. **Visible intent**: open a `Features/` folder and `SubmitSalesOrderCommand : ICommand<Unit>` next
to `GetSalesOrderQuery : IQuery<SalesOrderDto>` tells you which changes data without opening either file.
**A type-level hook**: you can write `where TRequest : ICommand<TResponse>` and have the compiler enforce that
a behavior applies to commands only. A marker interface isn't about methods — it's a label the compiler
understands.

**4. In `LoggingBehavior( ValidationBehavior( handler ) )`, which runs first, and how is that controlled?**

**Logging** runs first (it's outermost), and it's controlled by **registration order** — the first-registered
behavior ends up outermost. That's why `LoggingBehavior` is registered before `ValidationBehavior`: it means a
request that *fails* validation still gets logged. Remember each behavior gets two turns — once on the way in,
once on the way out — so outermost is **first in and last out**.

---

## Phase 3: Database

**1. 🎯 Which business rule is the *one* we deliberately enforce in SQL, and why can't it live only in C#?**

The **atomic inventory reservation** (`usp_Inventory_Reserve` — reserve only if available). It can't live in
C# because it isn't really a *policy* question, it's an **atomicity** one: any C# version has to read the
stock, decide, then write, and two requests can slip between the read and the write and both "win," overselling
the last unit. Only the database can make the check and the write one indivisible statement
(`UPDATE … WHERE (OnHand - Reserved) >= @Qty`). Note the shape of the distinction: *deciding whether something
is allowed* is domain work; *making sure two people doing it at once can't both succeed* is database work.

**2. What does the `RowVersion` column enable, and in which later phase?**

**Optimistic concurrency**, used in [Phase 8](08-approval-cancellation.md). SQL Server bumps the stamp on
every update to the row, so a write can say "update only if the stamp is still the one I read"
(`WHERE Id = @Id AND RowVersion = @RowVersion`). Rowcount 0 means someone changed it first — you're prevented
from silently overwriting their change (a **lost update**).

**3. Why copy `UnitPrice` onto the order line instead of always reading it from `Item`?**

So that changing an item's price tomorrow doesn't silently rewrite what customers were charged yesterday. The
line records the price **at order time** — a historical fact, not a live lookup. Real ERPs always snapshot
this; without it, last quarter's invoices change every time someone edits the catalog.

**4. Name two `CHECK` constraints and the bad data each makes impossible.**

Any two: `CK_Inventory_NotOver (Reserved <= OnHand)` makes overselling impossible at the data level, whatever
the app does; `CK_SalesOrderLine_Qty (Quantity > 0)` makes a zero/negative-quantity line impossible;
`CK_Customer_Balance (OutstandingBalance >= 0)` makes a negative balance impossible (so an over-release can't
drive it below zero); `CK_SalesOrder_Status (Status BETWEEN 0 AND 4)` makes a status outside the enum
impossible; `CK_AppUser_Role` makes an unknown role impossible. These are *integrity* backstops, not policy —
they hold even if your C# has a bug.

---

## Phase 3b: Data Access

**1. What are the two data-access paths, and which does a plain `SELECT` use?**

**Read**: `factory.Create()` → open → query → done, **no transaction**. **Write**: `factory.Create()` →
`OpenAsync` → `BeginTransaction()` → run the writes on that transaction → `Commit()`. A plain `SELECT` uses
the **read** path — there's nothing to roll back, so a transaction would only hold locks for no benefit.

**2. Why must a write that inserts an order header *and* its lines happen in a single transaction?**

Because they only make sense together. Fail after the header but before the last line and you've got a
half-written order — a header pointing at lines that don't exist, with nothing to undo it. The transaction
makes it **atomic**: every row commits together, or none does. Turnstile: all the way through, or still
outside — never stuck halfway.

**3. What does `Commit()` do, and what happens if you `Rollback()` — or never commit at all?**

`Commit()` makes every provisional statement in the transaction real, all at once, for everyone.
`Rollback()` throws the scratch space away — the rows vanish as if you'd never touched the database. And
**never committing does the same thing**: disposing an uncommitted transaction rolls it back, which is why the
`using` is a genuine safety net even if an exception skips past your `catch`.

**4. Why depend on `IDbConnectionFactory` instead of newing up a `SqlConnection`?**

Two reasons. Practically: one place knows how to build a connection, so the connection string isn't
copy-pasted through dozens of files. Architecturally: `new SqlConnection(...)` in a module would drag SQL
Server — and the connection string — onto that module's compile path, breaking the rule that a module sees
interfaces (`Shared.BuildingBlocks`) and never implementations (`Shared.Infrastructure`). The factory is the **port**;
`SqlConnectionFactory` is the only class in the solution allowed to say `SqlConnection`.

---

## Phase 4: Passwords and Access Tokens

**1. 🎯 A JWT's payload can be decoded by anyone. Why is that not a security hole, and what must you never
put in one?**

Because a JWT's job is to prove **authenticity**, not to hide information. It's *signed, not encrypted*:
anyone can read the claims, but nobody without the signing key can produce a token whose signature checks
out — so an attacker can read `role: SalesRep` but can't change it to `Admin` without the server noticing.
What you must never put in one: anything secret — passwords, API keys, card numbers, personal secrets. Assume
the payload is public, because it is.

**2. Why is PBKDF2 deliberately *slow*?**

Because the attacker has to run it too — and they have to run it *billions* of times. Making one verification
take a few hundred milliseconds is invisible to a user logging in once, but it multiplies a brute-force attack
from hours into centuries. Slowness is the feature. (It's also why you only pay it at *login*, and carry a
token afterwards.)

**3. Two users pick `Password123!`. Why don't they get the same stored hash, and what does that prevent?**

Because each password is hashed with its own random **salt**, so `Password123! + saltA` and
`Password123! + saltB` produce completely different hashes. It prevents **rainbow-table** attacks (precomputed
password→hash lookups are useless when every hash has a unique salt) and stops an attacker who cracks one
hash from learning that a thousand other users share that password.

**4. The stored hash starts with `600000.` — what is that for, and what breaks without it?**

It's the **iteration count** the hash was created with. Storing it alongside means `Verify` can reproduce the
hash with *the parameters that hash was actually made with*. Leave it out and you can never raise the work
factor: the day you bump 600,000 → 1,200,000, every existing password stops verifying, because you'd be
hashing old passwords with new parameters and getting different bytes. With it stored, old and new passwords
coexist happily.

**5. Both are interfaces in front of a single implementation. What does the login handler gain?**

Mainly **testability and a boundary**. The handler can be unit-tested with a fake hasher and a fake token
service — no crypto, no config, no clock. And the interfaces are what keep the implementations in
`Shared.Infrastructure` (where the signing key lives) instead of leaking into callers: the handler stays
ignorant of PBKDF2 and JWT internals, so those can change without touching it. "Only one implementation
today" is not an argument against an interface when the interface is what's enforcing the dependency rule.

---

## Phase 4e: Auth Wrap-Up

**1. Authentication vs. authorization, one sentence each.**

**Authentication** = *who are you?* (verifying identity — logging in, validating a JWT).
**Authorization** = *what are you allowed to do?* (checking whether that identity may perform this action).
Authentication always comes first: you can't decide what someone may do until you know who they are.

**2. Why does a handler talk to `ICurrentUser` instead of `HttpContext`?**

Because `HttpContext` is ASP.NET Core, and business code that touches it is welded to the web framework. You
could no longer unit-test "a rep may only see their own customers" without spinning up an HTTP request. The
port/adapter split — `ICurrentUser` in `Shared.BuildingBlocks`, `CurrentUser` (which reads `HttpContext`) in
`Shared.Infrastructure` — keeps handlers and the domain framework-free and lets a test pass a fake caller in
one line.

**3. Why must `UseAuthentication` come before `UseAuthorization`?**

Because authorization reads the identity that authentication produces. `UseAuthentication` validates the
token and populates `HttpContext.User`; `UseAuthorization` then checks that user's claims against the
endpoint's requirements. Reverse them and authorization runs against an empty identity — every role check
fails (or worse, silently passes) because there's nobody there yet.

**4. Give an authorization rule that belongs on the endpoint, and one that belongs in the handler.**

**Endpoint**: "only a `SalesManager` may approve orders" — it depends on nothing but the caller's role, which
is already in the token, so it can be answered before any code runs (`RequireRole("SalesManager", "Admin")`)
and rejected with a 403 before a row is loaded. **Handler**: "a manager may not approve an order they created
themselves" — the endpoint can't answer that, because it doesn't know who created the order until the handler
loads it. The test: *if two users with the same role should get different answers, the rule isn't an endpoint
rule.*

**5. 🎯 Why does rotating on every use make a stolen token detectable, and what happens on detection?**

Because rotation makes each refresh token **single-use**, so the legitimate client and the thief cannot both
keep using the same lineage — whoever refreshes first spends the token and leaves the other holding one that's
already revoked. When that spent token is presented, that's proof two parties held copies. The server then
revokes the **entire family** (every token descended from that login), forcing a fresh password login. You
never catch the theft in the act; you just guarantee the two copies must eventually diverge.

**6. Why hash a refresh token at all, yet why is plain SHA-256 fine when a password needs PBKDF2?**

**Hash it** so a database leak yields no usable tokens — same reason as passwords. **Plain SHA-256 is enough**
because the threat is different: a refresh token is 256 bits of cryptographic randomness, so it can't be
guessed or brute-forced, and there's nothing to protect against a dictionary attack. A password is short and
human-chosen — guessable — which is exactly what a slow, salted hash defends against. Right tool for the
actual threat; PBKDF2 on a random token would just waste CPU.

**7. Which column stops existing refresh tokens working after a password change, and why beat a sweep?**

The **`SecurityStamp`**. Each refresh token records the user's stamp at mint time; `usp_AppUser_ChangePassword`
sets `SecurityStamp = NEWID()`, so every outstanding token's snapshot stops matching and fails its next
refresh. It beats a "revoke all their tokens" sweep because (a) it's **one column comparison** instead of a
table write, and it can't miss a token that's mid-flight in another request; and (b) it's **self-healing** —
a token that somehow dodged a sweep still fails, because the value it vouches for simply no longer agrees. The
password change doesn't hunt tokens down; it invalidates the fact they all depended on.

**8. Why take the refresh token's absolute cap from `PasswordExpiresUtc` instead of `Jwt:RefreshAbsoluteDays`?**

Because the actual business rule is *"a session may not outlive the password"*, and password lifetime is
governed by a **database policy** (90 days from the last change). Reading the cap from that same column means
there's one source of truth. If both existed, you'd have the same policy written in two places that must be
kept in perfect sync forever — the **split-brain** [Phase 3](03-database-schema.md) warns about: change the DB
policy to 60 days, forget the config, and tokens quietly outlive the passwords they were minted under. One
policy, one home.

**9. What does an API key answer that a JWT doesn't, why must it run first, and why can't an SPA keep it secret?**

An API key answers **"which *client application* is calling?"**; a JWT answers **"which *user*?"** — different
questions, and they stack. It must run **before** authentication because it's the only gate that can guard
the anonymous doors: `/auth/login` *must* be `.AllowAnonymous()` (you can't require a token to get a token),
so a JWT structurally cannot protect it — and rejecting unknown clients first also avoids spending CPU
validating tokens for traffic you're about to drop. A **browser SPA can't keep it secret** because the key
ships inside the JavaScript bundle and rides on every request: anyone can read it from the network tab in ten
seconds. So it's a coarse outer filter (block anonymous internet traffic off your login door, tell clients
apart, rate-limit or revoke one client) — never the real security boundary. It's a true secret only where it
lives on a machine the public can't read: server-to-server, a backend-for-frontend, a partner integration.

**10. Why is `/auth/change-password` anonymous, and why is verifying the current password safe?**

Because requiring a token **re-creates the dead-end the endpoint exists to fix**: a user whose password has
expired can't log in, so they can't get a token, so they could never reach the one endpoint that would fix
their password. Making it anonymous is what lets it serve as both a routine change *and* the way back in.
It's safe — arguably *safer* than a token check — because three things still guard it: the API-key gate fires
first, the caller must present the **correct current password** (which a stolen access token never reveals),
and the deliberately slow PBKDF2 verify makes guessing expensive. Requiring the old password also stops a
leaked access token from being enough to *lock a user out of their own account*.

---

## Phase 5: Domain Model

**1. 🎯 Why is credit-limit a domain rule but inventory reservation a SQL one, when both need DB data?**

Because "needs data from the database" isn't the question — *what kind of risk are you defending against?* is.
The credit check is **pure arithmetic policy**: given a balance, a limit, and a total, is this allowed? The
handler loads the numbers, the domain decides, and it's fully unit-testable by constructing a `Customer` with
numbers. A slightly stale balance is a normal business risk, not a correctness bug. Inventory reservation is
an **atomicity** problem: two requests racing at microsecond scale can both read "8 available" and both
reserve 5, overselling stock — no amount of correct C# prevents that, only an indivisible statement does.
*Policy → domain. Atomicity under concurrency → SQL.*

**2. Why doesn't `SalesOrder.Submit()` reserve inventory itself?**

Because reserving safely requires an atomic *database* operation, and the domain is forbidden from touching
the database — it must stay pure and unit-testable. Put a DB call in `Submit()` and you couldn't test the
state machine without a database, and you'd have broken the dependency rule. The domain's job is to say "a
submit happened, and it was legal"; the **handler** orchestrates the reservation, inside a transaction.

**3. How does the design guarantee `Total` can never disagree with the lines?**

Because `Total` is **computed, not stored**: `_lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal)`.
There's no settable field to drift out of sync — the total is derived from the lines every time it's read, so
disagreement is unrepresentable. (The database *does* keep a `TotalAmount` snapshot for fast search, but the
domain's computed value is the authority; the column is a display convenience.)

**4. For a category restriction, where does the *fact* of the caller's role come from, and where is it judged?**

The **fact** comes from outside — the handler reads it from `ICurrentUser.Role`, which traces back to the JWT.
The **judgement** happens in the domain: `item.Category.EnsureRoleAllowed(currentUser.Role)`, where the
category decides and throws. That's the shape of every domain method here: **pass in facts, never decisions**.
Had we written `EnsureRoleAllowed(bool isAllowed)`, the caller would be doing the deciding and the rule would
have quietly escaped the domain — a `bool` parameter on a domain method is almost always a decision that got
away.

---

## Phase 6: Create Sales Order

**1. 🎯 Which checks are in the validator, which in the domain, and what's the dividing principle?**

**Validator**: shape only — `CustomerId > 0`, at least one line, `Quantity > 0`. **Domain**: every business
rule — blocked customer, credit limit, category role restriction, category quantity cap, draft-only line
edits. The principle: *does answering this require loading business data and knowing business policy?* A
validator asks "is this request well-formed?" and needs nothing but the request. A business rule asks "is this
operation allowed?" and belongs on the entity it's about. The practical reason it matters: a rule in a
validator can't be unit-tested with the entity it concerns, and it silently stops being enforced the moment
another slice writes the same data.

**2. How does an exception mid-handler roll back the database changes?**

The writes run on a transaction (`BeginTransaction()`), and nothing is real until `Commit()`. A throw skips
past the `Commit()` and lands in the `catch`, which calls `tx.Rollback()` — every provisional row vanishes.
And **if the `catch` is somehow bypassed**, the `using var tx` still disposes the transaction on the way out,
and *disposing an uncommitted transaction rolls it back*. The explicit `Rollback()` is there to make the
boundary visible and immediate; the `using` is the backstop. Belt and suspenders.

**3. Why accumulate category quantity across the whole order instead of per line?**

Because a per-line cap is trivially dodged by splitting: want 10 of a Controlled item capped at 5? Put 5 on
one line and 5 on another, and every line passes. The cap is a **per-order** rule, so it has to be checked
against the running total for that category across all lines.

**4. Trace what happens when Beta Industries orders 100 Widgets.**

Beta has a ₱5,000 limit and already owes ₱4,500 → ₱500 of headroom. 100 Widgets × ₱25 = **₱2,500**. The
handler loads Beta (exists ✓), `EnsureNotBlocked()` passes (Beta isn't blocked), loads Widget (General
category — no role restriction, cap 1,000), `EnsureRoleAllowed("SalesRep")` passes, running quantity 100 ≤
1,000 passes, `AddLine` brings the total to ₱2,500 — and then `EnsureWithinCreditLimit(₱2,500)` computes
4,500 + 2,500 = 7,000 > 5,000 and **throws a `DomainException`**. The handler exits before ever opening the
transaction, so nothing is written, and `GlobalExceptionHandler` maps it to a **422** with "would exceed the
credit limit. Available credit: ₱500.00." Same code, different data, correct business outcome.

---

## Phase 7: Submit and Concurrency

**1. 🎯 Why is one atomic SQL `UPDATE` safe where a C# read-then-write race-conditions?**

Because it removes the **gap**. The C# version is three separate trips: read the stock, decide in memory,
then write. Between the decision and the write, another request can do the same thing — both read "8
available," both conclude "5 ≤ 8," both reserve 5, and you've promised 10 units you don't have. Nothing in
that C# is wrong, and no unit test catches it, because a unit test runs one thread. The single statement
`UPDATE … SET Reserved = Reserved + @Qty WHERE ItemId = @ItemId AND (OnHand - Reserved) >= @Qty` puts the
*check* inside the very statement that does the *writing*, and the database's row lock means a second caller
can't even evaluate its `WHERE` until the first has committed. Check and act become inseparable — you inspect
`@@ROWCOUNT` to learn whether you won.

**2. Why must all the line reservations and the status change share one transaction?**

So a failure on line 3 undoes lines 1 and 2. Without it you'd have reserved stock for the first two lines and
then failed the submit — phantom reservations held by no order, quietly making stock unavailable forever.
Inside one transaction, the throw on line 3 rolls back *everything*, including the status change. Everything,
or nothing.

**3. Where does the rollback "happen" in the code — what triggers it?**

A **`throw`** triggers it. When `TryReserveAsync` returns false, the handler throws a `DomainException`;
control jumps out of the `try` to the `catch`, which calls `tx.Rollback()` and rethrows so the caller still
learns it failed. Nothing "detects" the failure — you write a correct `throw` and let the `catch` (backed by
the `using`, which rolls back any uncommitted transaction on dispose) undo the partial work.

**4. Why reserve at submit rather than at approval?**

So a manager is never asked to approve an order you then can't fulfill. Reserving at submit makes
"PendingApproval" genuinely mean *"we've set this aside for you."* If the order is later cancelled or
rejected, the reservation is **released** — that reserve→(approve | release) lifecycle is a real ERP pattern,
and the matched pair is what keeps stock honest.

---

## Phase 8: Approval and Cancellation

**1. 🎯 Why does approval need *both* a transaction and a `RowVersion` check?**

They solve different problems. The **transaction** makes *your own* writes all-or-nothing: the status change
and the balance adjustment commit together or not at all, so they can never disagree. The **`RowVersion`
check** stops you overwriting a change *someone else committed before you started* — a **lost update**. A
transaction gives you no protection there: you'd happily commit your change on top of theirs, atomically and
wrongly. Transaction = my writes are atomic. RowVersion = I'm not clobbering someone else's committed change.

**2. Why is the customer balance adjusted at approval rather than at create or submit?**

Because approval is the moment the company commits to the sale — that's when the customer genuinely owes the
money. At create it's a draft that might be abandoned; at submit it's pending and might be rejected. Tying the
balance change to approval, **in the same transaction as the status change**, keeps "Approved" and "balance
increased" perfectly consistent.

**3. Why is releasing stock guarded by `hadReservation`?**

Because a **Draft** order never reserved anything — reservation happens at submit. Releasing unconditionally
would *add* stock that was never taken, inventing units out of nothing every time someone cancelled a draft.
We release only if the order actually held a reservation (`PendingApproval` or `Approved`). Model reserve and
release as a matched pair, always — and the same logic explains the `wasApproved` guard on the balance
reversal.

**4. Which authorization here is endpoint-level, and why is that the right place?**

`RequireRole("SalesManager", "Admin")` on the **approve endpoint**. It's right there because the rule depends
on *nothing but the caller's role*, which is already in the token — no database needed. So it can be enforced
before any code runs: a SalesRep gets a **403** before `ApproveSalesOrderHandler` ever loads a row. Cheap,
central, and impossible to forget inside a handler.

---

## Phase 9: Update Order

**1. Why doesn't the update handler contain an `if (status != Draft)` check?**

Because the domain already guarantees it: `ClearLines()` and `AddLine()` throw
`"Lines can only be changed on a draft order."` if the order isn't a Draft. Re-checking in the handler would
duplicate a rule that already has a home — and the copy would eventually drift from the original. The handler
calls the domain and lets it refuse. If you catch yourself writing that `if`, delete it.

**2. Why re-run the credit/category checks on update instead of trusting the create-time result?**

Because the new lines are **new facts**. An edit can introduce an item from a restricted category the caller
can't order, blow the per-order quantity cap, or push the total past the credit limit. The create-time result
says nothing about a set of lines that didn't exist then. (State drifts too — the customer may have been
blocked since.)

**3. Why is a little duplication between create and update acceptable here?**

Because the two handlers *look* similar but answer different questions — "build a new order" vs. "re-line an
existing draft" — and forcing them into one "smart" method couples two things that should evolve
independently. Note what's actually shared: the **rules** (`EnsureNotBlocked`, `EnsureRoleAllowed`,
`EnsureQuantityAllowed`, `EnsureWithinCreditLimit`) already live in the domain and *are* reused. What's
duplicated is orchestration — and that's the cheap part to repeat and the expensive part to over-abstract.

**4. Why take the id from the route and the lines from the body?**

To avoid the "id in the URL disagrees with the id in the body" ambiguity — if both exist, which wins? Taking
the id only from the route means the question can't arise, and it's conventional REST: the URL identifies the
resource, the body carries the new state.

---

## Phase 10: Queries and Search

**1. Why does the query handler depend on `IDbConnectionFactory` and not the repository?**

Because the repository is a **write-side** tool — it deals in aggregates, rehydrating a full `SalesOrder` so
its rules can be enforced. A read enforces nothing, so building an aggregate just to flatten it into a DTO is
wasted work that also couples your screens to your write model. A query goes straight to Dapper and projects
the shape the screen actually wants.

**2. Why does the search proc return *two* result sets?**

Because a grid needs both "here are 20 rows" (the page, via `OFFSET/FETCH`) and "there are 137 total, so 7
pages" (the count). Returning both from one proc call gets them in **one round-trip** instead of two.

**3. Why does the API speak status *names* while the database stores *numbers*?**

Because the number is an internal storage detail. `?status=Approved` is readable, self-documenting, and stable
for clients; `2` is meaningless and — worse — silently *wrong* if anyone ever reorders the enum. Convert at
the boundary: names outside, numbers inside. Never leak raw enum values across an API.

**4. Why cap `PageSize`, and where is that enforced?**

To stop a client (careless or hostile) asking for a million rows and taking the server down with one request.
It's enforced in the **validator**: `RuleFor(x => x.PageSize).InclusiveBetween(1, 100)` — it's a *shape* rule
about the request, not a business rule, so the validator is exactly right. (Note it only actually *fires* once
[Phase 11](11-cross-cutting.md)'s `ValidationBehavior` runs it.)

---

## Phase 11: Cross-Cutting Concerns

**1. What do behaviors + one handler buy you vs. doing it per endpoint?**

You write logging, validation, and error mapping **once**, and every endpoint gets them — including every
endpoint you add later, for free. The per-endpoint alternative is copy-paste in a dozen files that drifts
apart: one endpoint forgets to log, another maps a `DomainException` to 500, a third never runs its validator.
This is the concrete answer to "what did the mediator actually buy me?" — the pipeline is the leverage.

**2. Why map `DomainException` → 422 but a random `Exception` → 500, and why hide the 500's detail?**

Because they mean opposite things. A `DomainException` is a **known, expected business outcome** — "Customer
is blocked" — the system working correctly and telling the user something useful; 422 says "I understood you,
and the business says no." A random exception is an **unexpected bug**; 500 says "this is my fault." You hide
the 500's detail because it might leak a table name, a file path, or a stack trace an attacker could use — so
the full detail goes to the server log where only operators see it, and the client gets a bland message.
Business messages are *meant* for the user, so those show verbatim in both environments.

**3. Why is `LoggingBehavior` registered before `ValidationBehavior`?**

Because first-registered is **outermost**, and you want logging to wrap validation — so a request that fails
validation is still logged. Register them the other way round and validation failures happen outside the
logger's view: they vanish from your logs, which is precisely the request you most wanted to see.

**4. Why can a handler safely assume its input is already valid?**

Because `ValidationBehavior` runs *before* it and throws on failure — if any rule fails, `next()` is never
called and the handler never runs. That's a real simplification: no null checks, no "is the quantity
positive?" defensive code. The handler starts from well-formed input and gets on with the use case.

---

## Phase 12: Testing

**1. 🎯 Why can almost all business rules be tested without a database, and what does that tell you?**

Because they're **pure domain methods** — `EnsureNotBlocked`, `EnsureWithinCreditLimit`, `EnsureRoleAllowed`,
the state machine — that operate on objects you can `new` up with numbers. No database, no host, no mocks;
they run in milliseconds. What it tells you is that the rules are **in the right place**. Turn it around and
it becomes the sharpest design check in the course: *if you need a database to test a business rule, the rule
is in the wrong place.* A rule that can only be tested through SQL has leaked out of the domain — into a proc,
a handler, or a validator.

**2. Which single test proves the concurrency guard, and why must it commit rather than roll back?**

`Two_concurrent_reservations_for_the_last_units_only_one_wins` — two real connections, each opening its own
transaction, both trying to reserve 5 of the 8 on hand; exactly one must succeed. It **must commit** because
committed-concurrency behavior is precisely what's being tested: from inside a transaction you're about to
undo, the second caller can't observe the first caller's committed state, so the race you're trying to
reproduce never happens. That's why this one test breaks the usual roll-back-for-isolation habit and cleans up
by hand instead. (It's also why it uses the *real* `StockReservationService` — a fake can't have a race
condition, so a fake would prove nothing.)

**3. Why is a "SalesRep gets 403 on approve" e2e test valuable?**

Because it proves **authorization is actually enforced through the real pipeline** — the token, the role
claim, `RoleClaimType`, the endpoint's `RequireRole`, all wired correctly — rather than merely configured.
Auth bugs are silent: a misconfigured `RoleClaimType` doesn't crash, it just quietly lets everyone through (or
nobody). Negative auth tests are the ones that catch real holes, which is why "only happy-path e2e" is a
mistake.

**4. What replaces EF migrations for setting up the test database?**

The module's own **ordered, guarded SQL scripts**, applied by the startup `DatabaseInitializer` (Phase 12 §4).
Each module ships its `Infrastructure/Sql/*.sql` as embedded resources; `CustomWebAppFactory` points the
connection string at `Erp_Test`, and booting the host creates and populates it. One set of scripts builds dev,
test, and production alike — that's the payoff of "migrations without EF." (In production you'd drive the same
scripts from a tracked deploy step, e.g. DbUp, rather than on boot.)

---

## Phase 13: Capstone

**1. Walk an order from login through approval, naming which layer enforces each rule and why.**

A full answer hits each of these, with the reason:
`X-Api-Key` → **middleware** (which *app* is calling; must run before auth because login is anonymous) →
`POST /auth/login` → **handler** (verify hash, mint tokens) → `POST /sales-orders` with the bearer token →
**endpoint** `RequireRole` (blanket "must be a sales role"; needs only the token) → **validator** (shape:
positive ids, ≥1 line) → **handler** (load customer + items — coordination, no policy) → **domain**
(`EnsureNotBlocked`, `EnsureRoleAllowed`, `EnsureQuantityAllowed`, `EnsureWithinCreditLimit`, `AddLine`'s
draft check — every business decision) → **handler** owns the transaction boundary → **SQL** persists →
`POST /submit` → **SQL** atomic reserve (concurrency safety) inside the handler's transaction → **domain**
`Submit()` (legal transition?) → `POST /approve` → **endpoint** `RequireRole("SalesManager","Admin")` (403 for
a rep, before any code runs) → **domain** `Approve()` (state machine) → **SQL** version-checked update (lost
updates) + balance adjust, one transaction → **after commit**, domain events dispatch (best-effort).
The through-line: *policy in the domain, atomicity in SQL, coordination in the handler, blanket role checks at
the door.*

**2. Explain, with the inventory example, why some rules *must* be in SQL.**

See [Phase 7 Q1](#phase-7-submit-and-concurrency). The short form: any C# check-then-act has a gap between the
check and the act, and two requests can both pass the check before either writes — overselling the last unit
with code that is not wrong and tests that all pass. Only the database can fuse the check and the write into
one indivisible statement. It's not that the rule is "about data"; it's that the rule must hold *under
concurrency*, and only one component in the system can guarantee that.

**3. Name three things this module still needs before production (no banned tech).**

Any three from the Phase 13 checklist, e.g.: **HTTPS enforcement** (`UseHttpsRedirection` — tokens must never
travel in the clear); **rate limiting on `/auth/login`** (built into ASP.NET Core — slow brute-force down);
**row-level authorization** (a rep should see only their own customers' orders — a data-dependent rule, so it
belongs in the handler/query); **credit limit counting in-flight orders** (today it only weighs the committed
balance, so ten pending orders can each pass individually); **version-checking `ReplaceLinesAsync`** (Phase 9
has no `RowVersion` guard, so two reps editing a draft means the second silently wins); **config validated at
startup** (fail the deploy, not the first request); **a health check that verifies the database**;
**structured logging to a real sink**. Note none needs Docker, Redis, or a broker.

**4. Give one concrete signal that it's time to add a message queue.**

When you have a **cross-system reaction that must not be lost** — e.g. "notify Accounting of every approval"
where a dropped message means a missing invoice. Phase 8's after-commit dispatch is explicitly *best-effort*:
if the process dies between the commit and the side effect, the event is gone. The moment "we lost one" is a
real business problem rather than a missing log line, you need guaranteed delivery (an outbox, then a broker).
The signal is a *requirement you can state*, not "we might scale later."

**5. Which Phase 2 decision makes swapping the engine — or extracting a service — a localized change?**

**Coding to the interface**: everything sends through `ISender` and handles through `IRequestHandler<,>`, so
nothing that *uses* the mediator knows which engine is behind it. The same instinct — depend on a published
contract, never on internals — is what makes extraction tractable: Sales reaches Inventory only through
`IStockReservation`, and each module owns its own schema, so there are no tables to untangle. One honest
caveat: the contract's `IDbTransaction` can't cross a network, so extraction reshapes *that* seam into a saga.
The wall still buys you most of the payoff.

---

## Phase 14: Going Further

**1. Which production gaps close with *no* new technology, and why is that most of the list?**

Nearly all of them: secrets in user-secrets/env vars with startup validation, HTTPS + HSTS, rate limiting,
a real health check, structured logging to a sink, row-level authorization. They're most of the list because
the hard part — a correct, rule-enforcing, concurrency-safe module — is *already done*, and what remains is
configuration and hardening that .NET ships in the box. The instinct worth keeping: reach for the platform you
have before you add a platform you don't.

**2. What does Docker buy you, and what does it explicitly not?**

It buys **reproducibility**: the same environment on every machine and in CI, a throwaway SQL Server nobody
has to install, and a deployable image. It does **not** make the module better, faster, or more correct — the
architecture is identical with or without it — and a container is not a security boundary on its own. It buys
environment parity, not quality. (Its first genuine win here is tests: Testcontainers replaces the
hand-managed `Erp_Test`.)

**3. Name a value you must never cache, and why.**

`Reserved` / `OnHand` in `inventory.Inventory`. Those are the exact values that must be read **live, under
concurrency**, inside the atomic reserve — cache them and you reintroduce the oversell bug Phase 7 exists to
kill, because you'd be deciding against a stale copy. The general rule: anything a correctness decision races
on can't be cached. A price is fine slightly stale; stock is not.

**4. Why does an in-memory rate limiter break with a second instance, and what fixes it?**

Because its counters live in **that process's memory**. Two instances behind a load balancer keep two separate
counts, so "5 attempts per minute" silently becomes 10 — and the guarantee scales with your instance count in
exactly the wrong direction. The fix is a **shared store** both instances consult (Redis being the usual
answer) — which is one of the two honest reasons Redis appears in this course at all.

**5. What can an outbox guarantee that after-commit dispatch cannot?**

That the event is **never lost**. Phase 8 dispatches *after* the transaction commits, so there's a window: if
the process dies between the commit and the side effect, the state change is durable but the reaction is gone
forever, silently. An outbox writes the event to a table **in the same transaction** as the state change — so
they're atomic together — and a background reader drains it, retrying until it succeeds. "After commit,
best-effort" and "guaranteed, eventually" are different promises; only one survives a crash.

---

## Phase 14b: Deploying to IIS

**1. 🎯 Under WebForms IIS *was* the runtime. What is it now, and what does ANCM do?**

IIS is now a **front door**, not a runtime. Your app is a self-contained program that already contains a web
server (Kestrel — that's why `dotnet run` served HTTP with no IIS anywhere), so IIS's job is reduced to
owning the public address, the TLS certificate, and the request queue. It has no idea how to execute a
`.dll`, which is why a naive copy-the-files deploy gets you a 500.19 or a file download. The **ASP.NET Core
Module (ANCM)** is the native IIS module that closes that gap: it **starts your app and hands it requests** —
in-process (loading .NET 10 inside `w3wp.exe`, the default) or out-of-process (reverse-proxying to a separate
Kestrel). It arrives with the Hosting Bundle, which is why installing that bundle is non-negotiable.

**2. Why "No Managed Code" for an app made entirely of managed code?**

Because the setting doesn't mean "no .NET here" — it means "**IIS**, don't *you* load a CLR." It controls
whether IIS loads the **.NET Framework** runtime into the worker process for its own managed pipeline. Your
app doesn't want that: ANCM loads **.NET 10** itself. Set it to v4.0 (the WebForms reflex) and you get a
handler load failure whose message never mentions the app pool.

**3. What does `web.config` configure now, and where does the connection string come from?**

It configures **IIS and ANCM only** — essentially two facts: "send every request to `AspNetCoreModuleV2`"
and "here's how to start the app." It says **nothing about your application**. The connection string, the
signing key, and the API keys come from `IConfiguration` — `appsettings.json` plus **environment variables**
(with `__` for nesting: `ConnectionStrings__ErpDb`). A `<connectionStrings>` section in `web.config` is
ignored *silently*, which is what makes it an afternoon-killer. Also: `web.config` is regenerated by
`dotnet publish`, so edits must go in the **project's** copy to survive a redeploy.

**4. 🎯 It works with `EXECUTE` on three schemas and no table permissions. Why — and what does it buy you?**

Because **every database call the app makes is `EXEC`ing a stored procedure** ([Phase 3](03-database-schema.md)),
and SQL Server's **ownership chaining** lets a caller with `EXECUTE` on a proc do whatever that proc does to
tables in the same schema — without holding any right on the tables. (It works precisely *because* the course's
rule holds that no proc reaches across schemas.) What it buys you: a SQL-injection hole or a stolen app
identity yields **only the operations you wrote procs for**. The attacker cannot `SELECT * FROM
[identity].AppUser`, cannot `DROP TABLE`, cannot read a column no proc returns. Compare an app running as
`db_datawriter` or `db_owner`: one hole and they have everything. This is Phase 3's stored-procedure choice
finally paying a security dividend — and most teams never collect it, because they grant `db_owner` on day
one to make the error go away.

**5. Why is startup `DatabaseInitializer` fine on a laptop and wrong on a server?**

Three reasons, and they compound. It wants to `CREATE DATABASE`, so the app's login would need **dbcreator or
sysadmin** — destroying the `EXECUTE`-only grant from Q4 for no benefit. It re-runs every script on **every
app-pool start**, including unattended 3am recycles. And with **two** servers, both race to apply schema
changes to one database. The rule: **schema changes belong to the deployment, not the application** — run the
scripts once, as a deploy step, with an account that has DDL rights, before the new version starts. Guard the
initializer to Development.

**6. 🎯 Why might the Phase 4c sweeper never run for weeks, with no error anywhere?**

Because an IIS app pool **shuts down after 20 minutes with no requests** (Idle Time-out) and recycles every
29 hours. A `BackgroundService` on a 6-hour `PeriodicTimer` lives inside that process, so a quiet night kills
it: the pool idles out, the process dies, the timer dies with it. Nothing throws, nothing logs — the
`RefreshToken` table just grows, and you find out months later via a performance complaint. The fix is either
the **SQL Agent job** (the database is always running and doesn't idle out — this is Phase 4c's "prefer Agent"
advice becoming concrete) or `idleTimeout=0` + `startMode=AlwaysRunning`. The transferable lesson:
**in-process background work has exactly the lifetime of its host process, and on a web server you don't own
that lifetime.**

**7. It works perfectly. Why deliberately trigger a 500?**

To prove `ASPNETCORE_ENVIRONMENT` is actually `Production`. Deployed as `Development`, **everything works
identically** — every endpoint, every rule, every test you'd think to run — while
[Phase 11](11-cross-cutting.md)'s handler quietly serves full stack traces to anyone who can cause an error.
It's a failure mode with no symptom on the happy path, so the *only* way to catch it is to look at an unhappy
one. You want a bland message and no stack trace. Finding it yourself beats an attacker finding it.

---

## Phase 15: Scaling the Wall

**1. What *is* the wall in the base course, and what replaces it here?**

Base course: the **absent project reference** — Sales's `.csproj` has no line pointing at Inventory, so
Inventory's types aren't on its compile path at all. This phase: a **project reference plus `internal`** —
Sales references the whole Inventory assembly, but everything except the published contract is `internal`, so
the compiler still refuses. The wall didn't come down; it **changed material**, from an absent reference to an
access modifier.

**2. Why doesn't the `Sales → Inventory` reference breach the wall here?**

Because of what's on the other side of it. `IStockReservation` is the only `public` type (plus the
`AddInventoryModule` composition seam); `StockReservationService` and everything else is `internal`, meaning
"visible only inside this assembly." So Sales can *name* the contract and nothing else — `new
StockReservationService()` in a Sales file is `error CS0122: inaccessible due to its protection level`. In the
base build the same reference *would* breach the wall, because everything there is `public`: the two halves
only work together.

**3. A test stops compiling when you mark a service `internal`. What's the fix, and why isn't `public`?**

Add `<InternalsVisibleTo Include="Erp.Integration.Tests" />` to the Inventory `.csproj` — a **guest list**
naming exactly the assemblies you trust with your internals. Making the service `public` again is wrong
because it undoes the entire phase to dodge one attribute: `public` opens the type to *every* assembly,
including Sales, which is precisely the door you just closed. Grant the key to one named, trusted assembly;
leave the wall standing for everyone else.

**4. A teammate adds `<InternalsVisibleTo Include="Erp.Modules.Sales" />`. What did they just undo?**

The wall — all of it. Sales can now reach every `internal` type in Inventory, so the module boundary is back
to a convention people are asked to respect rather than something the compiler enforces. It's the junk drawer
returning through the side entrance: the reference was only safe *because* `internal` made the contract the
sole reachable surface. A test assembly on that list is a deliberate hole; another **module** on it means
there's no wall.

**5. Two modules each need the other's contract. Why not reference each other, and what do you do instead?**

You can't: two implementation assemblies referencing each other is a **circular dependency**, and the compiler
rejects it outright (rightly — neither could build first). It's also a design smell worth heeding. The fix is
to pull the contracts into a small, dependency-free `Xxx.Contracts` project that both implementation
assemblies reference — contracts point one way, implementations depend on contracts, and the cycle disappears.
Never let two implementation assemblies point at each other.

**6. When is contracts-in-`Shared` the better choice, and how do you know you've outgrown it?**

It's better when you have **a handful of modules** and want a wall you can verify in five seconds: open the
`.csproj`, see no reference, done — no access modifiers, no `InternalsVisibleTo`, no ceremony. That clarity is
worth a lot, and it's why the base course teaches it first. You've outgrown it when the **junk-drawer test**
fails: open `Shared/Contracts`, point at an interface, and if you can't say which module owns it without
opening the file, ownership has become a guess. The other two signals: a one-line contract change recompiles
every module and drags every team into one merge queue, and a contract starts needing a type from its owning
module — at which point neutral `Shared` stops being neutral.

---

← Back to the [tutorial overview](00-START-HERE.md) · Stuck on a term? [GLOSSARY.md](GLOSSARY.md)
