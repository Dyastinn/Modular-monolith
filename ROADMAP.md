# Roadmap — YASDA, from reset to release

**YASDA** (*Yet Another Service Desk Application*) is an open-source service desk: requesters file tickets,
agents work queues, SLAs keep everyone honest, and a knowledge base answers the common questions before
they're asked. This file is the plan and the progress. The rules live in [CLAUDE.md](CLAUDE.md), the look in
[DESIGN.md](DESIGN.md).

Until 2026-11-19 this repo built a Philippine ERP. The user replaced it to leave every domain that needs BIR
registration or permits. The ERP is the git tag `erp-final`; its roadmap and log are in
[ROADMAP-HISTORY.md](ROADMAP-HISTORY.md).

**Order of work:** code first, lessons second, for the whole rebuild. Each milestone lands in the reference
build ([course-validation/business/](course-validation/business/)) with the build and every test suite green.
The course is written fresh from the finished code in Y8. The ERP's 25 lessons were deleted on 2026-11-19
(they're in the tag `erp-final`, a template for structure and voice if useful).

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

- **Y1 — Reset to YASDA** ✅ code (2026-11-19). Details below.
- **Y2 — Tickets core** ⬜: create a ticket (requester portal), queues with filters and server sorting,
  take/assign with a race test, status machine (New → Open ⇄ Pending → Resolved → Closed; reopen; cancel),
  replies and internal notes, optimistic concurrency. Restores the outbox delivery and redelivery tests with
  the first real event.
- **Y3 — Events, basic SLA, teams** ⬜: the Sla module on the outbox (`TicketCreated` starts 24/7 clocks);
  teams, categories and routing (category → team); email on new replies and assignments.
- **Y4 — Knowledge base** ⬜: articles, review flow, audit trail.
- **Y5 — Real SLA** ⬜: business hours, Philippine holidays as editable data, pause/resume, FsCheck
  properties; the breach scheduler with warnings, breaches and escalation.
- **Y6 — Attachments and search** ⬜: storage on a volume, limits, safe downloads; PostgreSQL full-text search
  over tickets and articles; suggested articles.
- **Y7 — Reports, security, deployment** ⬜: SLA compliance, response and resolve times, CSV export;
  requesters see only their own tickets (API tests); attachments in backups; `podman compose up` end to end.
- **Y8 — The lesson pass** ⬜: write lessons 01–25 from the finished build (mapping below), the start page,
  foundations, glossary, learning resources and answer key; a reader-facing version of DESIGN.md and the screen
  rules. Screenshots taken from the finished screens.
- **Later, after the course:** inbound email (MailKit), Assets, automation rules, satisfaction survey, live
  queue updates.

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
- [x] Cleared out (user, 2026-11-19): the 25 ERP lessons and companion pages, MUDBLAZOR.md and the ERP
      screenshots, BEST-PRACTICES-REVIEW.md, course-validation/FINDINGS.md, the unused `/api/users/directory`
      endpoint and `identity.user_directory()`, the unused FsCheck and FakeTimeProvider pins (back when a test
      needs them)
- Parked until Y2, honestly: the outbox delivery and redelivery tests (no event exists yet), the "every event
  has a handler" checks run on an empty set, and the concurrency test (no aggregate yet).

---

## Lesson mapping for Y8

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

- **There is no course until Y8.** By design (build first). README says so at the top; the risk is Y8 growing
  large, so keep notes per milestone of what each lesson must teach.
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
