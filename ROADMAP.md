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
  teams, categories and routing (category → team); email on new replies and assignments. **Planned in full
  below (2026-11-20); follow the plan, don't redesign it.**
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

### Y3 — Events, basic SLA, teams ⬜ (planned 2026-11-20)

This plan was made in advance so that each step is mechanical. **Every design decision is already made here.** If a
step seems to need a new one (a new event, table, role rule or package), stop and ask; don't invent it. **All
code follows CLAUDE.md §5 "Write plain, boring code. Never be clever":** if a step's code comes out clever, it isn't
done. Each step
ends with the named test project green; run the whole suite once at the end of each part.

#### Before you start

- [ ] **Step 0. Finish Y2 first.** (The 19 browser tests ran green on 2026-11-20; rerun them after the rename.) Make the D1 rename (below) in
      the Y2 code, regenerating the Tickets migration pair, so lessons 04–05 teach `Critical` from the start. Then
      write lessons 03–11 from Y2's code. Y3 rewrites `Ticket.Create`, the squashed Tickets migrations and `search_tickets.v1.sql`. Lessons
      written later would describe code that no longer exists. Then tag the commit: `git tag y2-done`.
      - [x] **D1 rename done (2026-11-20).** `Urgent` → `Critical` everywhere; Tickets migrations regenerated;
        all suites green (see the history log). Still open: lessons 03–11 and the tag.
- [ ] **Restore the `erp-final` tag.** It is missing locally and on `origin`, though its commit `ab58472` exists.
      The user runs `git tag erp-final ab58472` and `git push origin erp-final`. CLAUDE.md and this file both point
      at the tag.
- [x] **Decisions, confirmed by the user 2026-11-20:**

      | # | Question | Decision |
      |---|---|---|
      | D1 | Urgent vs DESIGN's "Critical"? | **Rename to `Critical`**, in Step 0, before lessons 03–11 are written, so no lesson has to change later |
      | D2 | "At risk" threshold (DESIGN §5.2 placeholder) | **Under 25% of the target's time left** |
      | D3 | Seeded SLA targets (24/7 in Y3, business hours from Y5) | Critical 1h / 4h · High 4h / 24h · Normal 8h / 72h · Low 24h / 120h (respond / resolve). **Database rows, never config**: seeded once by migration and edited by an Admin on `/sla-policies`, each row recording `updated_utc` and `updated_by_name` |
      | D4 | Is a category required on a new ticket? | **No.** "Not sure" leaves the ticket unrouted for a team lead to sort |
      | D5 | Does an edited SLA policy change open tickets? | **No, new tickets only.** A ticket's priority change recomputes its deadlines with the current policy |
      | D6 | Who manages what? | **Teams and their members: Admin.** Categories (routing) and SLA policies: Admin and TeamLead (`TicketRoles.Leads`). Every change records who and when |
      | D7 | Must the assignee be in the ticket's team? | **Yes, except Admins**, who may take or be assigned any ticket |
      | D8 | Reopen and the resolve deadline | **The original deadline stands.** Reopening can't reset a breach |
      | D9 | Who hears about work nobody has yet? | **The team's team leads, always**, plus the members the team's notify setting adds: none, selected members, or everyone. An unrouted ("Not sure") ticket goes to every TeamLead |

#### Design (decided; don't change without asking)

**Events.** Domain events are `internal` in each module's `Domain/` folder. Integration events are `public sealed
record`s in `.Contracts`, carrying only primitives, enums from the same Contracts project, and UTC `DateTime`s.
Domain events end in `Event` (`TicketCreatedEvent`); integration events don't (`TicketCreated`). That way the two
never share a name.

| Raised by `Ticket` (domain) | Published as (`Yasda.Modules.Tickets.Contracts`) | Heard by |
|---|---|---|
| `TicketCreatedEvent` in `Create` | `TicketCreated(long TicketId, TicketPriority Priority, DateTime CreatedUtc)` | Sla: start the clock |
| `FirstAgentReplyEvent` in `Reply`, when `FirstResponseUtc` is first set | `FirstAgentReply(long TicketId, DateTime RepliedUtc)` | Sla: stop the response target |
| `TicketResolvedEvent` | `TicketResolved(long TicketId, DateTime ResolvedUtc)` | Sla: stop the resolve target |
| `TicketReopenedEvent` (by `Reopen` and by a requester's reply to a resolved ticket) | `TicketReopened(long TicketId, DateTime ReopenedUtc)` | Sla: resolve target runs again, same due time |
| `TicketCancelledEvent` | `TicketCancelled(long TicketId, DateTime CancelledUtc)` | Sla: stop both, unmeasured |
| `TicketPriorityChangedEvent` | `TicketPriorityChanged(long TicketId, TicketPriority Priority, DateTime ChangedUtc)` | Sla: recompute running targets from `CreatedUtc` |
| `TicketAssignedEvent(Guid EventId, TicketId TicketId, string Subject, UserId AssigneeId, UserId AssignedById)` | none (stays in Tickets) | Tickets: queue an email |
| `ReplyAddedEvent(Guid EventId, TicketId TicketId, string Subject, UserId AuthorId, bool AuthorIsStaff, bool IsInternal, string Body, UserId RequesterId, UserId? AssigneeId)` | none (stays in Tickets) | Tickets: queue an email |

- `Yasda.Modules.Tickets.Contracts` declares `public enum TicketPriority { Low, Normal, High, Critical }`. The Tickets
  domain keeps its own internal enum (the domain depends only on BuildingBlocks). The publishing handler maps one
  to the other with a plain `switch`.
- **Sla has its own internal `Priority` enum in `Sla/Domain`** (same four names). Its integration handlers map from
  `Tickets.Contracts.TicketPriority` with a `switch`. Sla's domain never references a Contracts type (rule 4.4; the
  architecture test doesn't catch this, so take care).
- **Event handler classes never end in `Handler`.** `Every_feature_handler_is_registered` checks every `*Handler` in
  `.Features` as a concrete registration, and event handlers are registered by interface. Name them for what they
  do: `PublishTicketCreated`, `StartClockOnTicketCreated`, `StopResponseOnFirstReply`, `StoreDeadlines`,
  `RenameTeamCopies`, `EmailOnReply`.
- **`Assign`, `Take` and `Reply` take the actor.** `TicketAssignedEvent` needs `AssignedById`: `Take` passes the
  agent as both, and `Assign(agent, name, assignedBy, now)` passes the team lead. The auto-take inside `Reply` is a
  self-assignment too. Email goes out only when `AssigneeId != AssignedById`.
- Sla publishes one event, `Yasda.Modules.Sla.Contracts.DeadlinesChanged(long TicketId, int Version, DateTime
  ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet, DateTime? NextDueUtc, DateTime?
  AtRiskFromUtc)`. Tickets hears it. `null` for Met means still running; `NextDueUtc` is `null` once nothing is
  running.
- **Not raised in Y3:** `TicketPending` and resume (that's pause, in Y5), `TicketClosed` (it has no SLA effect),
  `SlaBreachImminent` and `SlaBreached` (the scheduler, in Y5). Raise an event only with its first handler.
- One publishing handler per domain event, in `Tickets/Features/Events/PublishTicketEvents.cs`. It holds several
  small classes, one per event.

**The id trap: why ticket ids come from a sequence.** `OutboxInterceptor` serializes events in `SavingChanges`,
*before* PostgreSQL assigns an identity column's value. So a `TicketCreatedEvent` raised in `Ticket.Create` would
carry `TicketId 0`.
- Fix: `b.HasSequence<long>("ticket_id_seq")` and `e.Property(x => x.Id).ValueGeneratedNever()`.
- Add `TicketsDbContext.NextTicketIdAsync(ct)`, which runs `SELECT nextval('tickets.ticket_id_seq')`.
- `Ticket.Create(TicketId id, …)` takes the id. The handler and the seed both call `NextTicketIdAsync` first.
- Test: `The_created_event_carries_the_ticket_number`.
- Message ids stay identity columns: no event needs them.

**Idempotency and order, handler by handler.** Delivery is at-least-once, and a failed message is retried after
later ones, so events can arrive twice and out of order.
- **Sla, `TicketCreated`:** the clock's primary key is `ticket_id` (`ValueGeneratedNever`). If a clock exists,
  return. If two deliveries race and one hits a duplicate key (`23505`), return.
- **Sla, every other event:** if there's no clock yet, throw `InvalidOperationException("No SLA clock for ticket
  #N yet")`, and the outbox retries it. **Order is not guaranteed.** Events from one `SaveChanges` share a timestamp
  (the seed saves Create, Take, Reply and Resolve together). A retried message is delivered after newer ones. Two app
  instances deliver in parallel. So expect a few of these retries, and their error log lines, on a seeded startup.
- **Each `SlaClock` method must be correct even when its event arrives after a later one for the same ticket.** Each
  one guards on the event's own timestamp:
  - `RecordResponse`: set it only if not set yet.
  - `RecordResolution(at)`: apply only if `at >= LastReopenedUtc`.
  - `Reopen(at)`: apply only if `at > ResolvedUtc`; it records `LastReopenedUtc`.
  - `Cancel` wins over everything, whatever the order.
  - `ChangeTargets(priority, at)`: apply only if `at > PriorityChangedUtc`.

  A stale call does nothing and leaves `Version` unchanged. Unit tests:
  `Resolve_and_reopen_in_reverse_order_leave_the_resolve_target_running` and
  `Two_priority_changes_in_reverse_order_keep_the_newer`.
- **Deadlines are computed from the event's timestamps** (`CreatedUtc`, `RepliedUtc`, …), never from the handler's
  `now`. A redelivery hours later must not move a deadline. Test:
  `A_late_redelivery_does_not_move_the_deadline`.
- **Tickets, `DeadlinesChanged`:** apply it only if `Version` is greater than the stored one
  (`TicketSla.Apply(...)` returns `false` otherwise).
- **Email:** the queue key is `"{kind}:{EventId}:{userId}"`, unique. Queueing the same key twice does nothing.

**Tickets' copy of the deadlines lives in its own table, `tickets.ticket_sla`**, not in columns on `ticket`.
- Columns: `ticket_id` PK (no FK to `ticket`; it is filled in later), `version`, `response_due_utc`,
  `response_met`, `resolve_due_utc`, `resolution_met`, `next_due_utc`, `at_risk_from_utc`.
- Why: writing it never touches the ticket row. So it never bumps the ticket's `xmin` (which would make an agent's
  save fail with 409) and never raises a Tickets event (which could loop back to Sla).
- Mapped as the plain entity `TicketSla` in `Tickets/Domain/TicketSla.cs`. Its one rule is `Apply(version, …)`.
- `search_tickets` and `get_ticket` `LEFT JOIN` it. That join stays inside the `tickets` schema, so the
  no-cross-schema rule holds.

**Breached and at risk are decided by Sla and compared at read time.** Sla computes `AtRiskFromUtc = due − 25% of
the target` (D2) and `NextDueUtc`; that's the rule, and it lives in Sla's domain. The screens only compare those
timestamps with "now". `Format.Sla(nextDue, atRiskFrom, now)` returns "Breached 40m ago", "1h 10m left" or "6h left",
plus a state for the CSS class. No SLA rule goes into SQL. Sorting by `next_due_utc` (nulls last) is just ordering.

**Requesters never see SLA** (DESIGN §5.2). `GetTicketHandler` and `SearchTicketsHandler` return the SLA fields as
`null` for non-staff. Test: `A_requester_never_receives_SLA_fields`.

**Teams belong to Identity; categories and routing belong to Tickets.**
- Identity: aggregate `Team` (`Id`, `Name` unique, `IsActive`) with members (`team_member(team_id, user_id)`).
  Members must be staff (Agent, TeamLead, Admin). Admin-only endpoints.
- `Identity.Contracts` gains `ITeamDirectory` with four methods:
  - `FindTeamAsync(long teamId)` → `TeamInfo(long Id, string Name, bool IsActive)?`
  - `TeamIdOfAsync(string name)`, for the Tickets seed, as `IdOfAsync(email)` is used for people
  - `IsMemberAsync(long teamId, long userId)`
  - `TeamIdsOfAsync(long userId)`
- `identity.list_staff` gains `p_team_id bigint` (null = all staff). `GET /api/staff?teamId=` feeds Assign's list.
- Identity also raises `TeamRenamedEvent` → integration `TeamRenamed(long TeamId, string Name)`. Tickets updates its
  copied team names with one `ExecuteUpdate` per table. This is idempotent: setting a name twice is harmless.
- Tickets: aggregate `Category` (`Id`, `Name` unique, `TeamId?`, `TeamName?`, `IsActive`). Routing *is* the
  category's team.
- `Ticket` gains `CategoryId?`, `CategoryName?`, `TeamId?` and `TeamName?`. On create, the team is the category's
  team.
- `Ticket.Route(category, team, assigneeIsMember)` (team lead, from the ticket page) changes the category and team.
  It keeps the assignee only when `assigneeIsMember`.
- `Take` and `Assign` on a ticket with a team require the assignee to be a member, unless the assignee is an Admin
  (D7). The handler asks `ITeamDirectory.IsMemberAsync` and passes two `bool`s in, `assigneeIsMember` and
  `assigneeIsAdmin`. The domain decides, and throws the new error code `TICKETS-0012 NotInTeam`. `Route` keeps an
  Admin assignee too.
- **Notify setting (D9).** `Team` gains `NotifyMode` (`LeadsOnly` default, `LeadsAndSelected`, `Everyone`), and
  `team_member` gains `notify bool` (used by `LeadsAndSelected`). Both are edited by an Admin on `/teams/{id}`. A
  team's leads are its members whose role is TeamLead. `ITeamDirectory` gains `NotifyListAsync(long? teamId)`,
  which returns user ids by the team's mode; with `null` (unrouted), it returns every active TeamLead. Identity
  decides the list, and Tickets only asks for it.
- The queue gains a Team filter: any, "My teams" (ids from `TeamIdsOfAsync`, passed as `bigint[]`), or one team.

**Email is queued, never sent inside an outbox delivery.** The dispatcher runs a message's handlers one after
another and retries all of them if one fails. If SMTP were down, Sla's clock would never start, and after 10 tries
the message would be parked.
- `Identity.Contracts` gains `IMailQueue.QueueAsync(string key, long toUserId, string subject, string body, ct)`.
  Identity looks up the address and skips disabled accounts. Tickets never sees an email address.
- The queue writes a row to `identity.outgoing_mail`: `key` PK, `to_address`, `subject`, `body`, `queued_utc`,
  `sent_utc`, `attempts`, `last_error`.
- The new `MailSender` `BackgroundService` in Identity sends the rows. It polls like the outbox
  (`FOR UPDATE SKIP LOCKED`, `Mail:PollIntervalMs`, `Mail:MaxAttempts`) and writes with EF.
- The MailKit code moves out of `SmtpEmailSender` into `SmtpMailer`, used by both. Password reset keeps sending
  directly, because the person is waiting for it.
- Recipients:
  - An agent's public reply goes to the requester.
  - A requester's reply goes to the assignee, or, if nobody has the ticket, to `NotifyListAsync(ticket.TeamId)`
    (D9).
  - A new ticket goes to `NotifyListAsync(ticket.TeamId)`. `TicketCreatedEvent` gains `Guid EventId`, `Subject` and
    `TeamId?`, and a second handler, `EmailOnNewTicket`.
  - Internal notes go to nobody.
  - Nobody gets mail about their own action: the author of a reply is removed from its list.
  - An assignment goes to the assignee, unless they assigned it to themselves (`Take`).
- The link in the email is `{App:PublicUrl}/tickets/{id}`. That's a new installation setting, allowed by §5.
- The honest cost goes into lesson 16: a crash between SMTP accepting a mail and `sent_utc` being saved sends it
  twice.

**SLA policies are rows.** The Sla aggregate `SlaPolicy` has `Priority` (unique), `RespondWithin` and
`ResolveWithin` (stored as minutes; `ResolveWithin ≥ RespondWithin > 0`, otherwise `SLA-0001`/`SLA-0002`), plus
`UpdatedUtc`, `UpdatedById` and `UpdatedByName` (who last changed it, shown on the screen). The
migration seeds one row per priority (D3); rows can be edited but never deleted. Screen: `/sla-policies`, for Admin
and TeamLead (D6).

#### Part A: the Sla module on the outbox (lesson 13)

- [ ] A1. Create the projects `Yasda.Modules.Tickets.Contracts` (empty for now) and `Yasda.Modules.Sla.Contracts`,
      then `Yasda.Modules.Sla`: `SlaModule` with `AddSlaModule()`/`MapSlaEndpoints()`, `SlaDbContext` (schema `sla`,
      its own `outbox_message`, `Configure`, design-time factory), `SlaDatabase : IModuleDatabase`, and `SlaErrors`
      (`SLA-0001…`).
      - Add all three to `Yasda.slnx`.
      - In `Program.cs`, register Sla **after** Tickets (registration order is migration order).
      - Add the new assemblies to the `Modules` and `ContractsAssemblies` arrays in `ModuleBoundaryTests` and
        `WiringTests`.
      - Green: architecture tests.
- [ ] A2. Ticket ids from the sequence (the id trap above). D1 was already done in Step 0.
      - The seed context gets the registered interceptors: inject `IEnumerable<IInterceptor>` into
        `TicketsDatabase` and add them in `Open()`. Otherwise seeded tickets write no outbox rows and get no clocks.
      - Green: Tickets unit tests.
- [ ] A3. Add the domain events in the table to `Ticket`, then the unit tests: each method raises exactly its event,
      a second `Reply` by an agent raises no second `FirstAgentReplyEvent`, and the FsCheck property still holds.
      Green: Tickets unit tests.
- [ ] A4. Sla domain: `SlaPolicy` and `SlaClock`.
      - `SlaClock` holds `TicketId`, `Priority`, `StartedUtc`, `RespondWithin`, `ResolveWithin`, `ResponseDueUtc`,
        `RespondedUtc?`, `ResolveDueUtc`, `ResolvedUtc?`, `Cancelled`, `Version` and `xmin`.
      - Methods: `Start`, `RecordResponse`, `RecordResolution`, `Reopen`, `Cancel`, `ChangeTargets`. Each change
        bumps `Version` and raises `DeadlinesChangedEvent`.
      - Due times are `StartedUtc + target` (24/7). Y5 replaces this with business-hours math.
      - Add a new test project, `Yasda.Modules.Sla.UnitTests`. It needs `FakeTimeProvider`: pin
        `Microsoft.Extensions.TimeProvider.Testing` again and record it in THIRD-PARTY-NOTICES.
      - Tests cover every method, met/missed exactly at the due minute (met), the at-risk point at 25%, and
        repeated calls as no-ops.
      - Green: Sla unit tests.
- [ ] A5. Wire the flow.
      - Tickets' publishing handlers.
      - Sla's `IIntegrationEventHandler<T>` for each Tickets event.
      - Sla's `PublishDeadlinesChanged`.
      - Tickets' `DeadlinesChangedHandler` → `TicketSla`.
      - Register each by hand.
      - Green: architecture tests (`Every_domain_event_has_a_handler…`, `Every_integration_event_is_handled_by_another_module`).
- [ ] A6. Migrations: regenerate the `InitialCreate` and `AddReadFunctions` pair for Tickets, and create Sla's
      (squash rule). Sla gets `sla.get_policies.v1.sql`. Tickets' `search_tickets`/`get_ticket` gain the SLA
      columns from `ticket_sla` and the sort key `nextDue` (allow-list).
- [ ] A7. Sla endpoints: `GET /api/sla/policies` (staff) and `PUT /api/sla/policies/{priority}` (Admin and
      TeamLead), plus the `/sla-policies` page.
- [ ] A8. Screens to DESIGN §5.2:
      - The queue's SLA column, with row markers for breached and at risk.
      - The SLA fact in the ticket header (staff only).
      - `Format.Sla`.
      - Add `Yasda.Web.UnitTests` (a Y2 gap) for `Format.Ago` and `Format.Sla`, and record it in this file.
      - The Y2 "SLA states and row markers" gap closes here.
- [ ] A9. Integration tests. Move `Eventually(...)` from `OperationsTests` into a shared helper. Then add:
      - `A_new_ticket_gets_deadlines_from_its_priority_policy`
      - `The_first_agent_reply_meets_the_response_target`
      - `Resolving_stops_and_reopening_restarts_the_resolve_target`
      - `A_cancelled_ticket_has_no_running_deadline`
      - `A_priority_change_recomputes_the_deadlines`
      - `A_late_redelivery_does_not_move_the_deadline`
      - `A_requester_never_receives_SLA_fields`
      - `The_queue_sorts_by_next_due`
      - The outbox tests parked since Y1:
        - `TicketCreated_delivered_twice_starts_one_clock`
        - `A_failing_handler_is_retried_then_parked`
        - `An_event_saved_before_a_crash_is_still_delivered` (save with the dispatcher not running, then run it)
        - `A_rolled_back_change_writes_no_outbox_row`
        - `Every_event_survives_the_round_trip_through_the_outbox` (each event type, serialized and read back)
      - `DatabaseTests`' seed-idempotency count must wait for the outbox to drain first, because clocks arrive
        afterwards.
      - Green: integration tests.

#### Part B: teams, categories, routing (lesson 15)

- [ ] B1. Identity: `Team` aggregate, `team_member`, `IdentityErrors` for a duplicate name or a non-staff member.
      Slices: `SearchTeams`, `GetTeam`, `CreateTeam`, `RenameTeam`, `SetTeamActive`, `AddTeamMember`,
      `RemoveTeamMember`, `SetTeamNotify` (mode plus each member's flag) (all Admin). `ITeamDirectory` with Dapper reads (`identity.get_team`, `identity.teams_of_user`).
      `TeamRenamedEvent` → `TeamRenamed`. Pages `/teams` and `/teams/{id}`. Regenerate Identity's migration pair.
      Seed: "Service desk" (agent, team lead) and "Network" (team lead).
- [ ] B2. Tickets: the `Category` aggregate with slices `SearchCategories`, `CreateCategory`, `UpdateCategory` (name,
      team, active; Admin and TeamLead) and the page `/categories`. Seed: Hardware, Software, Access → Service desk; Network →
      Network. Tickets' handler for `TeamRenamed` updates `category.team_name` and `ticket.team_name`.
- [ ] B3. `Ticket` gains its category and team (D4: optional). The New ticket form gets a "Category" `<select>` with
      "Not sure". The `RouteTicket` slice is for TeamLead and Admin. `TICKETS-0012 NotInTeam` applies to Take and
      Assign. Assign's staff list shows only team members when the ticket has a team. The queue gets a Team filter
      in the address.
- [ ] B4. Tests:
      - Unit: `Route` keeps or drops the assignee; Take and Assign outside the team are refused;
        `An_admin_may_take_any_teams_ticket`.
      - Integration: `NotifyListAsync` for each `NotifyMode`, and for `null` (every TeamLead).
      - Integration: routing on create, re-route, team filter, "My teams", rename propagates, non-staff can't
        join a team.
      - Browser: filing with a category lands in the team's queue.
      - Green: all.

#### Part C: email on replies and assignments (lesson 16)

- [ ] C1. Identity: `outgoing_mail`, `IMailQueue` + `MailQueue`, `SmtpMailer` (extracted), and the `MailSender`
      background service with a metric, `yasda.mail.sent`/`yasda.mail.failed`. With no `Smtp:Host`, it logs the
      mail and marks it sent (today's development behavior).
- [ ] C2. Tickets: the domain events `TicketAssignedEvent` and `ReplyAddedEvent`, each with an `EventId = Guid.NewGuid()`
      set in the domain, and the handlers `EmailOnAssignment` and `EmailOnReply` (recipient rules above).
      `App:PublicUrl` goes in `appsettings.json` and the deploy `.env` example.
- [ ] C3. Tests:
      - `An_agent_reply_queues_one_mail_to_the_requester`
      - `An_internal_note_queues_no_mail`
      - `A_new_ticket_mails_the_teams_leads`
      - `A_new_unrouted_ticket_mails_every_team_lead`
      - `A_requester_reply_to_an_unassigned_ticket_mails_the_notify_list`
      - `Everyone_mode_mails_every_member`
      - `Taking_a_ticket_yourself_queues_no_mail`
      - `Redelivering_a_reply_event_queues_it_once`
      - `A_disabled_assignee_gets_no_mail`
      - `Smtp_failure_retries_then_parks_the_mail_without_blocking_the_outbox` (a fake `SmtpMailer` that throws)
      - Green: all.

#### Part D: lessons (after A–C are green)

**The migrations differ between the build and the lessons.** Squashing and regenerating (A6, B1) is for the build
only. In the reader's project a shipped migration or `.v1.sql` is never edited (CLAUDE.md §5). Each schema change
in Parts A–C is a new migration in its lesson, and every changed read function gets a `.v2.sql`: `search_tickets`,
`count_tickets`, `get_ticket` and `identity.list_staff`. The switch from an identity column to a sequence is lesson
13's own migration, taught problem first: the event carries ticket id 0.

- [ ] **12 Containers and CI:** the code already exists (Testcontainers, Compose, CI). The lesson moves the
      reader's integration tests from the native PostgreSQL of lessons 05–11 to Testcontainers, and installs
      Podman.
- [ ] **13 Events and the outbox:** Part A.
- [ ] **14 Observability:** the code already exists. Add the outbox and mail metrics to what it shows.
- [ ] **15 Teams, categories, routing:** Part B.
- [ ] **16 Users and email:** Part C.
- [ ] Follow each lesson in a scratch folder. Add the ANSWER-KEY and GLOSSARY entries (outbox, at-least-once,
      idempotent, integration event, SLA, at risk). Then update the README and the history log.

### Y4–Y7: decisions made ahead (detail each milestone's steps when the one before it is done)

Confirmed by the user, 2026-11-20:

| # | Milestone | Decision |
|---|---|---|
| E1 | Y4 | **Any staff member drafts; a KbEditor publishes.** A KbEditor can't approve their own draft. Draft → In review → Published (→ Archived). Every published version is kept, with who and when |
| E2 | Y4 | **Each article is `Public` or `Internal`.** Requesters see only Public ones, enforced in the API and proven by API tests like ticket isolation |
| E3 | Y5 | **One calendar, fully editable on a screen** (Admin): time zone, working days, open and close time per day, and holidays. A country picker loads a seeded holiday preset; the Philippines ships (regular and special non-working days, this year and next). For other countries, an admin adds holidays by hand or imports a CSV. Default: Mon–Fri 08:00–17:00, Asia/Manila. **This changes CLAUDE.md §5:** the time zone moves from `Company:TimeZone` (config) to the calendar row, and the config value only seeds the first calendar. Update CLAUDE.md in the same change |
| E4 | Y5 | **Nothing is automatic by default.** Agents close and follow up themselves. A settings screen (Admin) turns each rule on: close Resolved tickets after N days (suggested 7, the reopen window); remind the requester on a silent Pending ticket after N days (suggested 7) and close it after M days (suggested 14). Breach warnings to the assignee and breaches to the team leads are always on |
| E5 | Y6 | **Attachments up to 10 MB:** images, PDF, Office documents, text and logs. No executables, scripts or archives. The type is checked from the content, not just the extension. The limit and the allowed types are admin-editable settings rows |
| E6 | Y6 | **Optional ClamAV virus scanning**, off unless `ClamAv:Host` is set. It runs as its own container (its GPL license doesn't bind YASDA). When it's on, an upload is scanned before it's stored, and an upload fails if the scanner is unreachable. Check the license of the client package before adding it |
| E7 | Y6 | **Search uses PostgreSQL's `simple` configuration with `unaccent`**, plus prefix matching, so Taglish works. No language stemming |
| — | Y7 | **Still open, on purpose: which reports exist, and the backup schedule.** The reports are built on what Y3–Y5 store (SLA clocks and their met/missed results, pause time, breaches, teams, categories), and backups must cover Y6's attachment volume. Decide both, with the user, when Y5 and Y6 are done. Don't design them earlier |
| E8 | Y7 | **Closed tickets are kept forever by default.** An Admin may set a retention period. A nightly job then deletes closed tickets older than that, with their attachments, and logs what it removed (Data Privacy Act) |

- **Y5 business time:**
  - The calendar of E3: working days and open and close times per day, in the calendar's own time zone.
  - `sla.holiday` rows: seeded from the chosen country's preset, editable on a screen.
  - A pure `BusinessCalendar` with `Add(start, duration)` and `Between(a, b)`.
  - FsCheck properties:
    - `Add` never lands outside open hours.
    - `Between(a, Add(a, d)) == d`.
    - Both are monotonic.
    - Holidays are skipped.
    - The same properties hold in a time zone with daylight saving.
  - Policies gain an optional per-category override (that's when `TicketCreated` gains `CategoryId`, not before).
  - In the lesson, the "before" is Y3's `StartedUtc + target` and the "after" is `calendar.Add(StartedUtc, target)`.
- **Y5 pause:**
  - `TicketPending` pauses the clock; a new `TicketResumed` (Pending → Open) resumes it. The clock adds up the
    paused business time and recomputes its due times on resume.
  - `DeadlinesChanged` gains `Paused`, and the screen shows "Paused".
- **Y5 scheduler:**
  - `SlaScheduler` (a `BackgroundService`) polls `sla.sla_clock WHERE next_check_utc <= now()` with
    `FOR UPDATE SKIP LOCKED`.
  - It sets `warned_utc` or `breached_utc` once, in the same `SaveChanges` as the `SlaBreachImminentEvent` or
    `SlaBreachedEvent`. Each event is created exactly once, even across restarts.
  - Warnings mail the assignee (or the team's notify list, D9); breaches mail the team's leads. Both go through
    `IMailQueue`, keyed by the event's id.
  - E4's auto rules run in the same scheduler, each one only when its setting is on.
  - Test: `A_restart_mid_breach_escalates_once`.
- **Y7 security pass:**
  - An endpoint × role matrix test lists every `/api` endpoint and asserts 401/403/404 for each role that mustn't
    use it.
  - A requester gets 404 on every ticket endpoint for someone else's ticket, never sees an internal note, and
    never sees an SLA field.
  - Attachment hardening (Y6) gets its own API tests: type sniffing, size limit, `Content-Disposition: attachment`,
    `X-Content-Type-Options`.

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
- [x] Screens aligned with the rewritten DESIGN.md (2026-11-20; see the history log). Browser tests updated (19),
      run against a live app on 2026-11-20: all green.
- [ ] DESIGN.md gaps that wait on data or a decision: SLA
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
