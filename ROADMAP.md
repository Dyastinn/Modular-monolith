# Roadmap — YASDA, from reset to release

**YASDA** (*Yet Another Service Desk Application*) is an open-source service desk: requesters file tickets,
agents work queues, SLAs keep everyone honest, and a knowledge base answers the common questions before
they're asked. This file is the plan and the progress. The rules live in [CLAUDE.md](CLAUDE.md), the look in
[DESIGN.md](DESIGN.md).

Until 2026-11-19 this repo built a Philippine ERP. The user replaced it to leave every domain that needs BIR
registration or permits. The ERP is the git tag `erp-final`; its roadmap and log are in
[ROADMAP-HISTORY.md](ROADMAP-HISTORY.md).

**Order of work (user, 2026-11-19):** each milestone lands its code in the reference build
([course-validation/business/](course-validation/business/)) with the build and every test suite green, **then
its lessons**, written from that working code and followed in a scratch folder to prove they build and pass. A
milestone is done only when both are. The repo exists to learn from, so the course is never far behind. The
course assumes the reader has nothing installed: each lesson installs the tools it first needs. The ERP's
lessons are in the tag `erp-final`, a template for structure and voice.

---

## Decisions (2026-11-19, user)

| Decision | Choice | Replaces |
|---|---|---|
| Product | **YASDA, a service desk**: tickets, SLAs, knowledge base. Nothing financial: no money, invoices, tax, fines or dues | The ERP (Sales, Inventory, Purchasing, Invoicing, BIR) |
| License | **MIT** for YASDA; every dependency permissive and free, nothing that ever costs money | "The source code is sold" |
| Architecture | Unchanged: modular monolith, vertical slices, `internal` modules behind Contracts, outbox, EF Core writes, Dapper reads, PostgreSQL | — |
| Front end | **Plain Blazor, no component library.** HTML elements, Blazor's own forms, one CSS file of DESIGN.md's tokens; lists are tables with link-based server sorting and paging; inline confirmations instead of dialogs. QuickGrid allowed but not used | MudBlazor (Material theme, MudDataGrid, dialogs, snackbars) |
| Look | [DESIGN.md](DESIGN.md): warm paper, one serif, blue links, no shadows or rounded corners, no dark mode | AppTheme (light/dark) |
| Rework | **In place**, on `main`; the ERP stays reachable at the tag `erp-final` | A fresh repo |
| Modules | Identity (kept), **Tickets**, **Sla**, **Knowledge**; later Assets and inbound Mail | Sales, Inventory, Purchasing, Invoicing |
| Roles | `Admin`, `TeamLead`, `Agent`, `KbEditor`, `Requester`; every account created by an admin | SalesRep, SalesManager, InventoryClerk, Purchaser, Accountant |
| Scheduler | A `BackgroundService` polling stored deadlines, like the outbox dispatcher; no Hangfire or Quartz | — |

Everything else in the ERP's decision table stands (PostgreSQL, EF Core migrations per module, Podman and
Compose, OpenTelemetry, testing in every lesson): see [ROADMAP-HISTORY.md](ROADMAP-HISTORY.md).

---

## Target modules

| Module | Owns | Talks to others through |
|---|---|---|
| **Identity** | Accounts, roles, teams, sign-in, the sign-in log, email | `Identity.Contracts`: user names, team membership |
| **Tickets** | Tickets, replies, internal notes, categories, priorities, tags, assignment, attachments; a copy of each ticket's SLA deadlines for sorting queues | Raises `TicketCreated`, `TicketPending`, `TicketReopened`, `FirstAgentReply`, `TicketResolved`; hears `DeadlinesChanged`, `SlaBreached` |
| **Sla** | Policies per priority and category, business-hours calendars, holidays, clocks, breaches | Hears ticket events; raises `DeadlinesChanged`, `SlaBreachImminent`, `SlaBreached` |
| **Knowledge** | Articles: draft → review → published; links to tickets; full-text search | `Knowledge.Contracts`: suggested articles for a ticket |

Reads never cross schemas: the "breaching soonest" queue sorts on Tickets' own copy of the deadlines, kept up
to date by `DeadlinesChanged`.

---

## Milestones

- **Y1 — Reset to YASDA** ✅ code · ✅ lessons 00–02 (2026-11-19). Details below.
- **Y2 — Tickets core** ✅ code (2026-11-19) · ⬜ lessons 03–11. Details below. Lesson 05 installs PostgreSQL,
  lesson 06 Playwright's browser; lesson 06 also adds the account row, `AuthorizeView` and the sign-in pages to
  lesson 01's layout. The outbox delivery and redelivery tests return in Y3 with the first real event.
- **Y3 — Events, basic SLA, teams** ⬜ · lessons 12–16 (12 installs Podman): the Sla module on the outbox (`TicketCreated` starts 24/7 clocks);
  teams, categories and routing (category → team); email on new replies and assignments.
- **Y4 — Knowledge base** ⬜ · lesson 17: articles, review flow, audit trail.
- **Y5 — Real SLA** ⬜ · lessons 18–19: business hours, Philippine holidays as editable data, pause/resume, FsCheck
  properties; the breach scheduler with warnings, breaches and escalation.
- **Y6 — Attachments and search** ⬜ · lessons 20–21: storage on a volume, limits, safe downloads; PostgreSQL full-text search
  over tickets and articles; suggested articles.
- **Y7 — Reports, security, deployment** ⬜ · lessons 22–25: SLA compliance, response and resolve times, CSV export;
  requesters see only their own tickets (API tests); attachments in backups; `podman compose up` end to end.
- **Y8 — Course polish** ⬜: a C# foundations page for readers new to C#, learning resources, screenshots of
  the finished screens, and one full read-through of all 25 lessons in order on a fresh machine.
- **Later, after the course:** inbound email (MailKit), Assets, automation rules, satisfaction survey, live
  queue updates.

### Y2 — Tickets core ✅ code (2026-11-19) · ⬜ lessons 03–11

- [x] `Yasda.Modules.Identity.Contracts`, the first contract: `IUserDirectory` (`IdOfAsync`, `NameOfAsync`,
      `FindStaffAsync`), implemented in Identity with Dapper; `GET /api/staff` (`identity.list_staff`)
- [x] `Yasda.Modules.Tickets`: the `Ticket` aggregate with its conversation (`TicketMessage`: replies and internal
      notes), statuses New → Open ⇄ Pending → Resolved → Closed, reopen within 7 days, cancel with a reason,
      take (refused if someone else has it), assign (team lead, staff only via the contract), priority; requesters
      see only their own tickets (404 otherwise); names copied onto the ticket so reads never cross schemas;
      `xmin` concurrency, with the ticket row touched when a message is added; error codes TICKETS-0001…0011
- [x] Reads: `tickets.search_tickets`/`count_tickets` (status, priority, me/unassigned, search; sort allow-list incl.
      priority by urgency via `tickets.priority_rank`), `tickets.get_ticket`/`get_ticket_messages` (notes for staff only)
- [x] Screens: the queue (filters, sort, paging in the address), New ticket, the ticket page (details, actions by
      role and status, inline cancel with a reason, priority, assign, conversation, reply or internal note); home
      and navigation per role
- [x] Development seed built through the domain (three tickets), user ids through the contract
- [x] Tests: Tickets unit 31 (incl. an FsCheck property over random action sequences, which found that an agent's
      reply opened a ticket with no owner, now fixed: replying to an unassigned ticket takes it, and only an
      Open/Pending ticket can be resolved), architecture 14 (Tickets + the Contracts project), integration 91 (life
      cycle, isolation, internal notes, the 10-round take race, a stale-copy save refused, assign rules, sorting),
      browser 16 (filing, take/reply/note seen correctly by each side, queue filters, inline cancel)
- [x] Readability pass over all code (user: "no fancy way of doing things", start to finish): property patterns,
      `??=`, one-line LINQ loops and condition-disabled submit buttons replaced by plain code; forms rely on
      `required` fields instead of buttons disabled until typed (that had broken in a real browser)
- [x] Screens aligned with the rewritten DESIGN.md (2026-11-20; see the history log). Browser tests updated (19)
      but **not yet run** against a live app since the change.
- [ ] DESIGN.md gaps that wait on data or a decision: priority **Urgent vs DESIGN's "Critical"** (renaming touches
      the domain, `priority_rank` and the squashed migrations; for now Urgent gets the Critical style); SLA
      breached/at-risk states and row markers (no SLA yet); KB search and popular articles on the requester Home;
      queue counts in the nav; bulk actions; "3 new tickets" live updates; the form error summary with focus
      (§5.4, forms still use the browser's `required`); a unit test project for `Yasda.Web` (`Format.Ago`)

### Y1 — Reset to YASDA ✅ code (2026-11-19)

- [x] Tag `erp-final` on the last ERP commit; work continues on `main`
- [x] Sales, Inventory, Purchasing and Invoicing removed with their tests, screens, SQL, deploy steps and
      `Money`, `Quantity`, `Tax`, `SpecialDiscount`, `DocumentNumber`, CSV import; their error codes (YASDA-0101…0107) dropped
- [x] `Erp` → `Yasda` everywhere in the build: solution, projects, namespaces, error prefix (`YASDA-`), database
      and roles (`yasda`, `yasda_app`, `yasda_owner`), env vars (`YASDA_*`), CI
- [x] Roles become Admin, TeamLead, Agent, KbEditor, Requester; Identity migrations regenerated (squash rule)
- [x] `GET /api/users/{id}` (`identity.get_user`) for the new user page
- [x] MudBlazor removed. Screens rebuilt in plain Blazor to DESIGN.md: masthead and link rows, status line,
      `SortHeader`, `Pager`, `PageHeader`, `PasswordField`; user dialogs became a user page with inline
      confirmation; the CSP drops `style-src 'unsafe-inline'` and `img-src data:`
- [x] Tests: unit 4, architecture 14, integration 69, browser 12 (new: sign-in, user admin, sorting through the
      address, error-code link, phone width, unsaved-changes guard); all green
- [x] MIT [LICENSE](LICENSE), [DESIGN.md](DESIGN.md) in the repo, CLAUDE.md, README, screen rules and
      third-party notices updated
- [x] Lessons 00 (start here), 01 (installing the tools from nothing, solution, plain-Blazor shell to DESIGN.md)
      and 02 (testing), plus ANSWER-KEY and GLOSSARY entries, all followed step by step in a scratch folder:
      lesson 01 builds with 0 warnings and serves its pages; lesson 02's 12 tests pass. Following them found two
      errors, now fixed: the Blazor template's "Restore failed" needed explaining, and `/tickets` answers 404
      until a page claims it (the ERP lesson had the same wrong claim)
- [x] Cleared out (user, 2026-11-19): the 25 ERP lessons and companion pages, MUDBLAZOR.md and the ERP
      screenshots, BEST-PRACTICES-REVIEW.md, course-validation/FINDINGS.md, the unused `/api/users/directory`
      endpoint and `identity.user_directory()`, the unused FsCheck and FakeTimeProvider pins (back when a test
      needs them)
- Parked until Y2, honestly: the outbox delivery and redelivery tests (no event exists yet), the "every event
  has a handler" checks run on an empty set, and the concurrency test (no aggregate yet).

---

## Lesson mapping (old ERP lesson → YASDA lesson)

| # | Was (ERP, tag `erp-final`) | Becomes (YASDA) |
|---|---|---|
| 01 | Solution setup | Same, named YASDA, plain Blazor shell to DESIGN.md |
| 02 | Testing | Same, new examples |
| 03 | Features (slices) | First slice: create a ticket |
| 04 | Domain model | The Ticket aggregate and its status machine |
| 05 | Database | The `tickets` schema |
| 06 | Sign-in | Same, new roles |
| 07 | Create order | Requester portal: file a ticket |
| 08 | Find orders | Agent queues: filters, server sorting, paging in the address |
| 09 | Submit | Take or assign, with a race test |
| 10 | Approve/reject/cancel | Pending, resolve, reopen, cancel |
| 11 | Update order | Replies, internal notes, edits, optimistic concurrency |
| 12 | Containers and CI | Same |
| 13 | Events and outbox | Sla module: `TicketCreated` starts the clocks |
| 14 | Observability | Same |
| 15 | Customers | Teams, categories, routing |
| 16 | Users and email | Same, plus reply and assignment emails |
| 17 | Item catalog (audited) | Knowledge base with review and audit |
| 18 | Shipping | Business hours, holidays, pause/resume; FsCheck |
| 19 | Purchasing | Breach scheduler and escalation |
| 20 | Invoicing | Attachments |
| 21 | Payments | Full-text search, suggested articles |
| 22 | Special discounts | Reports and CSV export |
| 23 | Security hardening | Same, plus row-level access and attachment hardening |
| 24 | Deploy | Same, plus attachments in backups |
| 25 | Production checklist | Same, without the BIR items |

---

## Risks to retire early

Retired risks are in [ROADMAP-HISTORY.md](ROADMAP-HISTORY.md).

- **A milestone's lessons cost as much as its code.** Writing and then following each lesson in a scratch folder
  is slow; it's also the only proof the course works. Don't mark a milestone done without it.
- **Install steps go stale** (package names, winget ids, Ubuntu's .NET source). Recheck lesson 01's commands
  on each OS before a release.
- **Business-hours math is easy to get subtly wrong** (time zones, a clock paused over a holiday, a deadline
  landing at closing time). Retire it in Y5 with FsCheck properties before any screen depends on it.
- **The breach scheduler must survive restarts** and never double-escalate: deadlines in the database, an
  idempotent escalation, a restart test.
- **Full `podman compose up` not executed yet** (carried over from the ERP): run the install end to end on a Linux
  server before the first release.
- **`podman exec` and crun 1.31** (carried over): if integration tests hang, check `rpm -q crun` first; fix
  on the machine, never in the code.

---

## Log

One dated line per landed change goes in [ROADMAP-HISTORY.md](ROADMAP-HISTORY.md), at the end of its log.
