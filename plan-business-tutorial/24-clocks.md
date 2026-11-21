# Lesson 24 — Deadlines in Open Hours, and the Pause

Good day, developer! Since lesson 14 a ticket has had two deadlines, and both were wall-clock arithmetic: the start plus the target. Last time we built the calendar and gave it a screen. Today the deadlines finally use it. A ticket filed on Friday afternoon is due on Monday. When an Admin adds a holiday, the deadlines of tickets already running move with it. And a ticket that is waiting on the requester stops its clock, because the team cannot be late for an answer only the requester can give. It takes about three hours. Are you ready? Let's begin!

## What we'll build

| Piece | What it is |
|---|---|
| **Deadlines in open time** | A clock asks the calendar where "4 open hours after the start" lands, and stores the answer |
| **The at-risk point, in open time** | "At risk" is three quarters of the way through a target's open time, and it is stored with the deadline |
| **Recalculation** | Saving the calendar or a policy works out every running clock again, in the same save |
| **Pause and resume** | Two new events from Tickets stop and start a clock. The paused time is open time, added to the deadlines |
| **"Paused" on screen** | The queue and the ticket page say it, instead of showing a deadline that is not running |

## Why we build it this way

### The pain: two things that make a stored deadline lie

A deadline is stored in a row, not kept in a timer. That is what lets it survive a restart, and lesson 25 depends on it. But a stored deadline is a copy of an answer, and the answer can change under it. The calendar can change: the Admin adds a holiday, and every deadline that counted that day as open is now wrong. And the ticket can change: it can go to "waiting on the requester", and the time it waits should not count against the team.

The simplest fix is to leave the deadline alone and subtract things when you show it. That puts the rule in every screen, every report and the scheduler, and one of them will forget.

### The fix: the clock owns its due times, and recomputes them

The clock keeps both due times and both at-risk points as columns. Whenever something that affects them happens, the clock works them out again from its start, and tells the rest of the system what changed (the `DeadlinesChanged` event, version by version, as in lesson 14). Everything else reads the answer.

### Now, let's be honest about the cost here

Recalculation loads every running clock into memory and works each one out again. For a help desk with a few thousand open tickets that is fine, and it keeps the code readable. With hundreds of thousands it would have to move into SQL. The second cost: the time a ticket was paused is kept as a duration, so if the calendar changes later, a pause that already ended is not counted again under the new calendar. We take that trade, because the alternative is keeping every pause and replaying it.

## 1. The clock counts open time

Lesson 14's clock added the target to the start. Now each due time comes from the calendar. Here is the clock as it is after this first half of the lesson; the pause arrives in step 5, so nothing about it is here yet:


```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Domain/SlaClock.cs
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// The two deadlines of one ticket: answer it, and resolve it. A target counts open time only: the due time is the business
// calendar's answer to "this much open time after the start". The events that move a clock arrive at least once, in any
// order, so each method guards on the event's own time and a stale call changes nothing.
internal sealed class SlaClock : AggregateRoot<TicketId>
{
    // A target is at risk when less than a quarter of its open time is left, which is three quarters of the way through it.
    private const double AtRiskAfterFraction = 0.75;

    private SlaClock() { }

    public Priority Priority { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public TimeSpan RespondWithin { get; private set; }
    public TimeSpan ResolveWithin { get; private set; }
    public DateTime ResponseDueUtc { get; private set; }
    public DateTime ResponseAtRiskUtc { get; private set; }
    public DateTime? RespondedUtc { get; private set; }
    public DateTime ResolveDueUtc { get; private set; }
    public DateTime ResolveAtRiskUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public bool Cancelled { get; private set; }
    public int Version { get; private set; }

    // Kept so a late event can be recognized as late.
    public DateTime? LastReopenedUtc { get; private set; }
    public DateTime? PriorityChangedUtc { get; private set; }

    // null while the target is still running, or when the ticket was cancelled (it is no longer measured).
    public bool? ResponseMet
    {
        get
        {
            if (Cancelled || RespondedUtc is null) return null;
            return RespondedUtc <= ResponseDueUtc;
        }
    }

    public bool? ResolutionMet
    {
        get
        {
            if (Cancelled || ResolvedUtc is null) return null;
            return ResolvedUtc <= ResolveDueUtc;
        }
    }

    // The earliest deadline still running; null once nothing is.
    public DateTime? NextDueUtc
    {
        get
        {
            if (Cancelled) return null;
            if (RespondedUtc is null) return ResponseDueUtc;
            if (ResolvedUtc is null) return ResolveDueUtc;
            return null;
        }
    }

    // When the running target (the one NextDueUtc names) enters its last quarter.
    public DateTime? AtRiskFromUtc
    {
        get
        {
            if (Cancelled) return null;
            if (RespondedUtc is null) return ResponseAtRiskUtc;
            if (ResolvedUtc is null) return ResolveAtRiskUtc;
            return null;
        }
    }

    // The ticket was created: both targets start running. The time is the ticket's own, never the handler's "now".
    public static SlaClock Start(TicketId ticketId, Priority priority, DateTime createdUtc, TimeSpan respondWithin, TimeSpan resolveWithin,
        BusinessCalendar calendar)
    {
        createdUtc = WholeMicroseconds(createdUtc);
        var clock = new SlaClock
        {
            Id = ticketId,
            Priority = priority,
            StartedUtc = createdUtc,
        };
        clock.SetResponseTarget(respondWithin, calendar);
        clock.SetResolveTarget(resolveWithin, calendar);
        clock.Changed();
        return clock;
    }

    // The first answer to the requester. Only the first counts.
    public void RecordResponse(DateTime atUtc)
    {
        if (Cancelled) return;
        if (RespondedUtc is not null) return;

        RespondedUtc = WholeMicroseconds(atUtc);
        Changed();
    }

    // The ticket was resolved. Resolving does not count as a response: only an agent's public reply stops the response target
    // (decision of 2026-11-20), so a ticket fixed with no reply keeps showing its unanswered target.
    // A resolution that happened before the last reopen is old news, and one already known is a repeat.
    public void RecordResolution(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null) return;
        if (LastReopenedUtc is not null && atUtc < LastReopenedUtc) return;

        ResolvedUtc = atUtc;
        Changed();
    }

    // The ticket was reopened: the resolve target runs again, with the same due time. A reopen that happened before the
    // resolution it would undo is old news; one that arrives before its resolution is only remembered.
    public void Reopen(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null && atUtc <= ResolvedUtc) return;

        if (LastReopenedUtc is null || atUtc > LastReopenedUtc) LastReopenedUtc = atUtc;
        if (ResolvedUtc is null) return;

        ResolvedUtc = null;
        Changed();
    }

    // Cancelled wins over everything, whatever order the events came in. Nothing is measured any more.
    public void Cancel()
    {
        if (Cancelled) return;

        Cancelled = true;
        Changed();
    }

    // The priority changed: the targets still running are recomputed from the start, with the policy's new numbers. A
    // target that is already finished keeps its due time. An older change that arrives late is ignored.
    public void ChangeTargets(Priority priority, DateTime atUtc, TimeSpan respondWithin, TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (PriorityChangedUtc is not null && atUtc <= PriorityChangedUtc) return;

        PriorityChangedUtc = atUtc;
        var before = Snapshot();

        Priority = priority;
        if (RespondedUtc is null) SetResponseTarget(respondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(resolveWithin, calendar);

        if (before == Snapshot()) return;

        Changed();
    }

    // The rules changed (the calendar, or the policy's numbers): the targets still running are worked out again from the start.
    // A target that is already finished keeps its due time and its numbers.
    public void Recalculate(TimeSpan respondWithin, TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        if (Cancelled) return;
        var before = Snapshot();

        if (RespondedUtc is null) SetResponseTarget(respondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(resolveWithin, calendar);

        if (before == Snapshot()) return;

        Changed();
    }

    // True while at least one target is still running: a clock whose deadlines can still move.
    public bool IsRunning => !Cancelled && (RespondedUtc is null || ResolvedUtc is null);

    private (Priority, TimeSpan, DateTime, DateTime, TimeSpan, DateTime, DateTime) Snapshot()
    {
        return (Priority, RespondWithin, ResponseDueUtc, ResponseAtRiskUtc, ResolveWithin, ResolveDueUtc, ResolveAtRiskUtc);
    }

    private void SetResponseTarget(TimeSpan within, BusinessCalendar calendar)
    {
        RespondWithin = within;
        ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within));
        ResponseAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction));
    }

    private void SetResolveTarget(TimeSpan within, BusinessCalendar calendar)
    {
        ResolveWithin = within;
        ResolveDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within));
        ResolveAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction));
    }

    // PostgreSQL keeps whole microseconds; a time with finer ticks would compare differently before and after the clock is
    // saved, and a reply made exactly at the due time would count as late. Every time that comes in is cut the same way.
    private static DateTime WholeMicroseconds(DateTime value)
    {
        const long TicksPerMicrosecond = 10;
        return new DateTime(value.Ticks - value.Ticks % TicksPerMicrosecond, value.Kind);
    }

    private void Changed()
    {
        Version++;
        Raise(new DeadlinesChangedEvent(Id, Version, ResponseDueUtc, ResponseMet, ResolveDueUtc, ResolutionMet,
            NextDueUtc, AtRiskFromUtc));
    }
}
```

The changes from lesson 14 are small and all in the same direction:

- `Start` takes a `BusinessCalendar`. `SetResponseTarget` and `SetResolveTarget` do the same thing for their target: remember the number, then ask the calendar for the due time and for the at-risk point.
- The at-risk point is **three quarters of the way through the target's open time**, and it is a stored column. It used to be worked out from the due time (due minus a quarter of the target), which only works when time is continuous. In open time, "a quarter left" can be across a weekend.
- `RecordResponse` and `RecordResolution` have not changed yet. They will in step 5.
- `ChangeTargets` (a priority change) and the new `Recalculate` both use the same two setters.

`Recalculate` is the new one. The rules changed (the calendar, or a policy's numbers), so the targets still running are worked out from the start again. A target that is already finished keeps its due time, because a met target must not move later and a missed one must not move earlier. And if nothing came out different, it raises nothing, so a recalculation that changes no deadline sends no event.

Two columns are new, so we need a migration. The model already has them; let EF Core write it:

```bash
dotnet ef migrations add AddOpenTimeDeadlines --project src/Modules/Sla/Yasda.Modules.Sla --output-dir Infrastructure/Migrations
```

An existing row needs a value in a new `NOT NULL` column, and EF Core would give every old clock the year 1. A clock that says "at risk since the year 1" would warn everyone tomorrow. So we edit the generated migration to say what old rows get: the point three quarters of the way, as the wall clock sees it, until the next change works out the real one:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/Migrations/<timestamp>_AddOpenTimeDeadlines.cs (the file EF Core made; replace its content)
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yasda.Modules.Sla.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenTimeDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "resolve_at_risk_utc",
                schema: "sla",
                table: "sla_clock",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "response_at_risk_utc",
                schema: "sla",
                table: "sla_clock",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // The clocks that already exist get the point three quarters of the way from their start to their due time, as the wall
            // clock sees it. It is only a stand-in: the next change to the calendar or a policy, or to the ticket, works the real
            // open-time points out again. Without this, every old clock would say "at risk since the year 1".
            migrationBuilder.Sql(
                """
                UPDATE sla.sla_clock
                SET response_at_risk_utc = started_utc + (response_due_utc - started_utc) * 0.75,
                    resolve_at_risk_utc = started_utc + (resolve_due_utc - started_utc) * 0.75;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolve_at_risk_utc",
                schema: "sla",
                table: "sla_clock");

            migrationBuilder.DropColumn(
                name: "response_at_risk_utc",
                schema: "sla",
                table: "sla_clock");
        }
    }
}
```

One thing to watch out for: a migration that adds a non-null column to a table with rows must say what the old rows get. The reference build squashes its migrations, so it never has old rows, but you do.

## 2. The handlers give the clock the calendar

The clock does not read the database. The handlers that call it do. `SlaDbContext` gets one helper that loads the calendar and turns it into the pure class:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/SlaDbContext.cs (added)
    // The organization's calendar as the pure class the deadline math runs on.
    public async Task<BusinessCalendar> GetBusinessCalendarAsync(CancellationToken ct)
    {
        var calendar = await Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);
        return calendar.ToBusinessCalendar();
    }
```

The calendar is two lists, so loading it with `Include` twice would make EF Core join them, and a week with 18 holidays would come back as 126 rows. We tell EF Core to use separate queries instead, once, in `Configure`:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/SlaDbContext.cs (added)
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
            // The calendar loads two lists (days and holidays); separate queries keep it from loading their product.
            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        options.UseSnakeCaseNamingConvention();
        return options;
    }
```

Now the handlers that start and retarget a clock ask for the calendar first:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Features/Events/ClockEventHandlers.cs
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Yasda.Modules.Sla.Features;

// What Sla does when Tickets says something happened. The outbox delivers each message at least once, so each class
// must give the right answer when it hears the same thing twice, and the clock itself guards against a message that
// arrives after a later one (see SlaClock). These run in the outbox delivery, which is why none of them sends mail.

internal sealed class StartClockOnTicketCreated(SlaDbContext db) : IIntegrationEventHandler<TicketCreated>
{
    public async Task HandleAsync(TicketCreated integrationEvent, CancellationToken ct)
    {
        var ticketId = new TicketId(integrationEvent.TicketId);
        var alreadyStarted = await db.Clocks.AnyAsync(c => c.Id == ticketId, ct);
        if (alreadyStarted) return;

        var priority = PriorityMapping.From(integrationEvent.Priority);
        var policy = await db.Policies.SingleAsync(p => p.Priority == priority, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);

        // The due times come from the ticket's own time, never from now: a delivery that comes hours late must not move them.
        var clock = SlaClock.Start(ticketId, priority, integrationEvent.CreatedUtc, policy.RespondWithin, policy.ResolveWithin, calendar);
        db.Clocks.Add(clock);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two deliveries of the same message raced, and the other one started the clock first. That is the answer we wanted.
        }
    }
}

internal sealed class StopResponseOnFirstReply(SlaDbContext db) : IIntegrationEventHandler<FirstAgentReply>
{
    public async Task HandleAsync(FirstAgentReply integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.RecordResponse(integrationEvent.RepliedUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class StopResolveOnTicketResolved(SlaDbContext db) : IIntegrationEventHandler<TicketResolved>
{
    public async Task HandleAsync(TicketResolved integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.RecordResolution(integrationEvent.ResolvedUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class RestartResolveOnTicketReopened(SlaDbContext db) : IIntegrationEventHandler<TicketReopened>
{
    public async Task HandleAsync(TicketReopened integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Reopen(integrationEvent.ReopenedUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class StopClockOnTicketCancelled(SlaDbContext db) : IIntegrationEventHandler<TicketCancelled>
{
    public async Task HandleAsync(TicketCancelled integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Cancel();
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class ChangeTargetsOnPriorityChanged(SlaDbContext db) : IIntegrationEventHandler<TicketPriorityChanged>
{
    public async Task HandleAsync(TicketPriorityChanged integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var priority = PriorityMapping.From(integrationEvent.Priority);

        // A priority change uses the policy as it is now (decision D5); only new tickets keep an edited policy's old numbers.
        var policy = await db.Policies.SingleAsync(p => p.Priority == priority, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.ChangeTargets(priority, integrationEvent.ChangedUtc, policy.RespondWithin, policy.ResolveWithin, calendar);
        await db.SaveChangesAsync(ct);
    }
}
```

## 3. Recalculate when the rules change

When an Admin saves the calendar, or a team lead saves a policy, every clock still running must be worked out again. One small class does it, and the handlers that change rules call it before they save, so the new rules and the new due times are saved together or not at all:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Features/Calendar/ClockRecalculator.cs
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

// When the calendar or a policy changes, every clock that is still running is worked out again under the new rules (decision
// F10). The caller has changed the calendar or the policy but not saved yet, so the new rules and the new due times are
// saved together, or not at all.
//
// Now, let's be honest about the cost here: this loads every running clock into memory. For a help desk with a few thousand open
// tickets that is fine and keeps the code plain; with hundreds of thousands it would have to move into SQL.
internal sealed class ClockRecalculator(SlaDbContext db)
{
    public async Task RecalculateAsync(BusinessCalendar calendar, CancellationToken ct)
    {
        var policies = await db.Policies.ToListAsync(ct);
        var running = await db.Clocks
            .Where(c => !c.Cancelled && (c.RespondedUtc == null || c.ResolvedUtc == null))
            .ToListAsync(ct);

        foreach (var clock in running)
        {
            var policy = policies.Single(p => p.Priority == clock.Priority);
            clock.Recalculate(policy.RespondWithin, policy.ResolveWithin, calendar);
        }
    }
}
```

Register it with the other handlers in `AddSlaModule`:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/SlaModule.cs (added)
        services.AddScoped<ClockRecalculator>();
```

The calendar handlers call it right before `SaveChangesAsync`. Here is the one for the week; the holiday handlers and the import handler get the same two lines:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Features/Calendar/ChangeCalendarHandler.cs
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record DayHours(DayOfWeek Day, TimeOnly? Opens, TimeOnly? Closes);
internal sealed record ChangeCalendarCommand(string TimeZoneId, List<DayHours> Days);

internal sealed class ChangeCalendarHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time, CompanyZone companyZone,
    ClockRecalculator recalculator)
{
    public async Task HandleAsync(ChangeCalendarCommand cmd, CancellationToken ct)
    {
        var calendar = await db.Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);

        var days = new List<WorkDay>();
        foreach (var hours in cmd.Days)
        {
            days.Add(new WorkDay(hours.Day, hours.Opens, hours.Closes));
        }

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        calendar.Change(cmd.TimeZoneId, days, new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);

        // New hours move the running clocks' deadlines, so they are saved in the same step.
        await recalculator.RecalculateAsync(calendar.ToBusinessCalendar(), ct);
        await db.SaveChangesAsync(ct);

        // Saved, so from now on "today" means today in the new zone.
        companyZone.Change(TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId));
    }
}
```

And a policy change calls it too:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Features/ChangePolicy/ChangePolicyHandler.cs
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record ChangePolicyCommand(Priority Priority, int RespondMinutes, int ResolveMinutes);

internal sealed class ChangePolicyHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time,
    ClockRecalculator recalculator)
{
    // false when there is no policy for that priority. Whether the numbers are allowed is the policy's call.
    public async Task<bool> HandleAsync(ChangePolicyCommand cmd, CancellationToken ct)
    {
        var policy = await db.Policies.SingleOrDefaultAsync(p => p.Priority == cmd.Priority, ct);
        if (policy is null) return false;

        // The name is copied onto the policy, so the screen can say who changed it without asking Identity again.
        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        policy.Change(TimeSpan.FromMinutes(cmd.RespondMinutes), TimeSpan.FromMinutes(cmd.ResolveMinutes),
            new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);

        // New numbers move the running clocks' deadlines (decision F10), so they are saved in the same step.
        var calendar = await db.GetBusinessCalendarAsync(ct);
        await recalculator.RecalculateAsync(calendar, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

This changes something you were told in lesson 14: a policy change used to apply only to tickets filed after it. Now it reaches tickets still running as well, because otherwise tickets filed before today's change would keep yesterday's promise forever. The policies page says so, so change its description:

```razor
@* src/Yasda.Web/Pages/Admin/SlaPolicies.razor (added) *@
        How long a team has to answer a new ticket and to resolve it, for each priority, counting open hours only (see Business hours).
        A change applies to tickets still running as well: their deadlines are worked out again with the new numbers.
```

## 4. Prove it: deadlines in open hours

This is a good moment to fix the tests that counted 24 hours a day. Lesson 13's `A_new_ticket_starts_a_clock_from_its_priority_policy` asserted that the due time is the start plus four hours. At 20:00 that is no longer true. Delete it, and its `ClockRow` record: the test below replaces it. The others in `SlaTests` get a small helper instead, which asks the same calendar class where a deadline should land. That way they pass at any hour:

```csharp
// tests/Yasda.Integration.Tests/SlaTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Integration.Tests;

// A ticket's deadlines, from the ticket being filed to the screen that shows them: Tickets raises an event, the outbox
// delivers it, Sla works the deadlines out, and the answer travels back the same way. Every step is a moment later, so the
// tests wait for what they expect instead of guessing how long to sleep.
[Collection("Integration")]
public class SlaTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Admin = "admin@yasda.local";

    private sealed record Created(long Id);
    private sealed record Sla(DateTime ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet,
        DateTime? NextDueUtc, DateTime? AtRiskFromUtc);
    private sealed record TicketView(long Id, string Status, string Priority, DateTime CreatedUtc, Sla? Sla);
    private sealed record Row(long Id, DateTime? NextDueUtc, DateTime? AtRiskFromUtc);
    private sealed record Page(List<Row> Items);

    // The installation's calendar (Monday to Friday, 08:00 to 17:00, Manila). The tests ask it where a deadline should land, so they
    // pass at any time of day: counting open hours is the calendar's job, and its own tests prove it.
    private static readonly BusinessCalendar OfficeHours = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"),
        new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Tuesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Wednesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Thursday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Friday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        },
        []);

    // The database keeps whole microseconds, so the ticket's time is cut the same way before counting.
    private static DateTime DueAfter(DateTime createdUtc, TimeSpan openTime)
    {
        var whole = new DateTime(createdUtc.Ticks - createdUtc.Ticks % 10, DateTimeKind.Utc);
        return OfficeHours.Add(whole, openTime);
    }

    private static async Task<long> File(HttpClient client, string priority, string? subject = null)
    {
        var response = await client.PostAsJsonAsync("/api/tickets",
            new { subject = subject ?? $"Deadlines {Guid.NewGuid():N}", message = "Please help.", priority });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Created>();
        return created!.Id;
    }

    // Reads the ticket until the deadlines Sla worked out have arrived and satisfy the test, then hands back what it read.
    private static async Task<TicketView> ReadUntil(HttpClient staff, long id, Func<TicketView, bool> arrived)
    {
        var ticket = await Waiting.Eventually(() => staff.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, arrived);
        return ticket!;
    }

    private static bool HasDeadlines(TicketView ticket)
    {
        return ticket.Sla is not null;
    }

    [Fact]
    public async Task A_new_ticket_gets_deadlines_from_its_priority_policy()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");

        var ticket = await ReadUntil(agent, id, HasDeadlines);

        // High is 4 open hours to respond and 24 to resolve (decision D3), counted from when the ticket was filed. It is at risk
        // for the last quarter of the open time, so from 3 open hours on.
        ticket.Sla.Should().NotBeNull();
        ticket.Sla!.ResponseDueUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(4)));
        ticket.Sla.ResolveDueUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(24)));
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResponseDueUtc);
        ticket.Sla.AtRiskFromUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(3)));
        ticket.Sla.ResponseMet.Should().BeNull();
        ticket.Sla.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public async Task The_first_agent_reply_meets_the_response_target()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        await ReadUntil(agent, id, HasDeadlines);

        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "On my way." })).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.ResponseMet is not null);

        ticket.Sla!.ResponseMet.Should().BeTrue();
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResolveDueUtc, "with the response done, the resolve target is next");
    }

    [Fact]
    public async Task Resolving_stops_and_reopening_restarts_the_resolve_target()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Looking." })).EnsureSuccessStatusCode();
        await ReadUntil(agent, id, t => t.Sla?.ResponseMet is not null);

        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        var resolved = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is not null);
        resolved.Sla!.ResolutionMet.Should().BeTrue();
        resolved.Sla.NextDueUtc.Should().BeNull("nothing is left running");

        (await requester.PostAsync($"/api/tickets/{id}/reopen", null)).EnsureSuccessStatusCode();
        var reopened = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is null);
        reopened.Sla!.ResolutionMet.Should().BeNull();
        reopened.Sla.NextDueUtc.Should().Be(reopened.Sla.ResolveDueUtc, "the resolve target runs again, with the same due time");
        reopened.Sla.ResolveDueUtc.Should().Be(resolved.Sla.ResolveDueUtc);
    }

    // Decision of 2026-11-20: resolving does not count as a response. A ticket fixed with no public reply keeps showing its
    // unanswered response target; only an agent's reply stops it.
    [Fact]
    public async Task A_ticket_resolved_without_a_reply_keeps_its_response_target_running()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        await ReadUntil(agent, id, HasDeadlines);
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is not null);

        ticket.Sla!.ResolutionMet.Should().BeTrue();
        ticket.Sla.ResponseMet.Should().BeNull();
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResponseDueUtc);
    }

    [Fact]
    public async Task A_cancelled_ticket_has_no_running_deadline()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Low");
        await ReadUntil(agent, id, HasDeadlines);

        (await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "Fixed itself" })).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.NextDueUtc is null);

        ticket.Sla!.NextDueUtc.Should().BeNull();
        ticket.Sla.AtRiskFromUtc.Should().BeNull();
        ticket.Sla.ResponseMet.Should().BeNull("a cancelled ticket is not measured");
        ticket.Sla.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public async Task A_priority_change_recomputes_the_deadlines()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Low");
        var before = await ReadUntil(agent, id, HasDeadlines);
        before.Sla!.ResponseDueUtc.Should().Be(DueAfter(before.CreatedUtc, TimeSpan.FromHours(24)));

        (await agent.PutAsJsonAsync($"/api/tickets/{id}/priority", new { priority = "Critical" })).EnsureSuccessStatusCode();
        var after = await ReadUntil(agent, id, t => t.Sla?.ResponseDueUtc != before.Sla.ResponseDueUtc);

        // Critical is 1 open hour to respond and 4 to resolve, still counted from when the ticket was filed.
        after.Sla!.ResponseDueUtc.Should().Be(DueAfter(after.CreatedUtc, TimeSpan.FromHours(1)));
        after.Sla.ResolveDueUtc.Should().Be(DueAfter(after.CreatedUtc, TimeSpan.FromHours(4)));
    }

    [Fact]
    public async Task A_requester_never_receives_SLA_fields()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");
        await ReadUntil(agent, id, HasDeadlines);

        var detail = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        var queue = await requester.GetFromJsonAsync<Page>($"/api/tickets?search={id}&pageSize=100");

        detail!.Sla.Should().BeNull();
        queue!.Items.Should().NotBeEmpty();
        queue.Items.Should().OnlyContain(t => t.NextDueUtc == null && t.AtRiskFromUtc == null);
    }

    [Fact]
    public async Task The_queue_sorts_by_next_due()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var word = Guid.NewGuid().ToString("N");
        var low = await File(requester, "Low", $"{word} slow");
        var critical = await File(requester, "Critical", $"{word} urgent");
        var high = await File(requester, "High", $"{word} soon");
        var cancelled = await File(requester, "Normal", $"{word} gone");
        foreach (var id in new[] { low, critical, high, cancelled })
            await ReadUntil(agent, id, HasDeadlines);
        (await requester.PostAsJsonAsync($"/api/tickets/{cancelled}/cancel", new { reason = "Not needed" })).EnsureSuccessStatusCode();
        await ReadUntil(agent, cancelled, t => t.Sla?.NextDueUtc is null);

        var soonestFirst = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={word}&sort=nextDue");
        var latestFirst = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={word}&sort=nextDue&desc=true");

        soonestFirst!.Items.Select(t => t.Id).Should().Equal(critical, high, low, cancelled);
        latestFirst!.Items.Select(t => t.Id).Should().Equal(low, high, critical, cancelled);
    }

    // PostgreSQL keeps whole microseconds, and the time in an event has finer ticks. A clock reloaded from the database has
    // its due time cut to the microsecond, so a reply made exactly at the due time once counted as late. Chosen ticks make it
    // happen every time, not only when the clock's last digit is unlucky.
    [Fact]
    public async Task A_reply_made_exactly_at_the_due_time_is_on_time_after_the_database_round_trip()
    {
        var ticketId = UniqueTicketId();
        // Wednesday 09:00 in Manila, inside open hours, so the 4 open hours end the same day at 13:00.
        var filed = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc).AddTicks(8);
        await DeliverTicketCreatedAsync(ticketId, filed);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<FirstAgentReply>>();
            // High is 4 hours to respond: this reply is exactly at the due time.
            await handler.HandleAsync(new FirstAgentReply(ticketId, filed.AddHours(4)), CancellationToken.None);
        }

        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        var met = await conn.ExecuteScalarAsync<bool?>("SELECT response_met FROM tickets.ticket_sla WHERE ticket_id = @ticketId", new { ticketId });
        met.Should().BeTrue();
    }

    [Fact]
    public async Task The_seeded_tickets_have_deadlines_too()
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);

        var clocks = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sla.sla_clock");
        var copies = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tickets.ticket_sla");

        clocks.Should().BeGreaterThanOrEqualTo(3);
        copies.Should().BeGreaterThanOrEqualTo(3);
    }

    // A message can be delivered late and more than once, and a deadline must not move for either: it is worked out from the
    // time the ticket was filed, never from the moment of delivery.
    [Fact]
    public async Task A_late_redelivery_does_not_move_the_deadline()
    {
        var ticketId = UniqueTicketId();
        var filedTwoDaysAgo = DateTime.UtcNow.AddDays(-2);

        await DeliverTicketCreatedAsync(ticketId, filedTwoDaysAgo);
        await DeliverTicketCreatedAsync(ticketId, filedTwoDaysAgo.AddHours(30));

        await using var conn = factory.OpenDatabase();
        var due = await conn.QuerySingleAsync<DateTime>("SELECT response_due_utc FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId });
        due.Should().BeCloseTo(DueAfter(filedTwoDaysAgo, TimeSpan.FromHours(4)), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TicketCreated_delivered_twice_starts_one_clock()
    {
        var ticketId = UniqueTicketId();
        var filed = DateTime.UtcNow;

        // Task.WhenAll on purpose: two deliveries at the same moment is the case where both see "no clock yet".
        await Task.WhenAll(DeliverTicketCreatedAsync(ticketId, filed), DeliverTicketCreatedAsync(ticketId, filed));

        await using var conn = factory.OpenDatabase();
        var clocks = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId });
        clocks.Should().Be(1);
    }

    private async Task DeliverTicketCreatedAsync(long ticketId, DateTime filedUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TicketCreated>>();
        await handler.HandleAsync(new TicketCreated(ticketId, TicketPriority.High, filedUtc), CancellationToken.None);
    }

    // Far above any number the ticket sequence will reach, and different every run.
    private static long UniqueTicketId()
    {
        return 1_000_000_000L + Random.Shared.NextInt64(1, 1_000_000_000L);
    }
}
```

The clock's own tests do not need a database. Many of the old ones keep working with a calendar that is open every hour of every day, so counting time like a wall clock is still easy to read. The new ones use the office week:

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (the top of the new file; the tests below go inside the class)
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Yasda.Modules.Sla.Domain;

namespace Yasda.Modules.Sla.UnitTests;

// One ticket's two deadlines. Every method must give the right answer when its event arrives twice, or after a later one.
public class SlaClockTests
{
    private static readonly TicketId Ticket = new(7);
    private static readonly TimeSpan Respond = TimeSpan.FromHours(4);
    private static readonly TimeSpan Resolve = TimeSpan.FromHours(24);

    // Open every hour of every day (to the last tick before midnight), so these tests can count time like a wall clock. The business
    // hours themselves are tested in BusinessCalendarTests and at the end of this file.
    private static readonly BusinessCalendar AllDay = AlwaysOpen();

    private static BusinessCalendar AlwaysOpen()
    {
        var week = new Dictionary<DayOfWeek, OpenHours>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            week[day] = new OpenHours(new TimeOnly(0, 0), TimeOnly.MaxValue);
        }
        return new BusinessCalendar(TimeZoneInfo.Utc, week, []);
    }

    // The ticket is created at 09:00; the test moves this clock to say when the other things happen.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private SlaClock NewClock()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve, AllDay);
        clock.ClearEvents();
        return clock;
    }

    private static DeadlinesChangedEvent OnlyEventOf(SlaClock clock)
    {
        clock.DomainEvents.Should().ContainSingle();
        return (DeadlinesChangedEvent)clock.DomainEvents[0];
    }
    // ... the tests that follow
```

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (added)
    [Fact]
    public void Deadlines_count_open_hours_only_so_a_friday_afternoon_ticket_is_due_on_monday()
    {
        var friday4pm = ManilaTime(11, 20, 16);

        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());

        // One hour on Friday, three on Monday morning. 24 open hours: 1 on Friday, then Monday, Tuesday (9 each), Wednesday 5.
        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 23, 11));
        clock.ResolveDueUtc.Should().Be(ManilaTime(11, 25, 13));
    }
```

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (added)
    [Fact]
    public void The_at_risk_point_is_three_quarters_through_the_open_time_not_the_wall_clock_time()
    {
        var friday4pm = ManilaTime(11, 20, 16);

        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());

        // 3 open hours after Friday 16:00 is Monday 10:00: the last open hour (10:00 to 11:00) is the at-risk hour.
        clock.AtRiskFromUtc.Should().Be(ManilaTime(11, 23, 10));
    }
```

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (added)
    [Fact]
    public void Changing_the_calendar_moves_the_running_deadlines_and_tells_everyone_once()
    {
        var friday4pm = ManilaTime(11, 20, 16);
        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());
        clock.ClearEvents();

        // The 23rd becomes a holiday: the response that was due Monday 11:00 is now due Tuesday 11:00.
        clock.Recalculate(TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek(new DateOnly(2026, 11, 23)));

        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 11));
        var changed = OnlyEventOf(clock);
        changed.Version.Should().Be(2);
        changed.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 11));
    }
```

| Test | Why it matters |
|---|---|
| `A_ticket_made_on_a_holiday_starts_counting_at_the_next_opening` | A holiday is not an open hour |
| `Recalculating_under_the_same_rules_changes_nothing` | No change, no event, no new version |
| `Recalculating_leaves_a_finished_target_with_its_due_time_and_its_numbers` | A met target never moves |
| `A_cancelled_clock_ignores_a_recalculation` | Nothing is measured any more |
| `A_clock_is_running_until_both_targets_are_finished` | `IsRunning` is what recalculation asks |

And through the real database, one test per rule that moves a deadline:

```csharp
// tests/Yasda.Integration.Tests/SlaCalendarTests.cs (added)
    // Decision F10: the calendar is data, and a change to it moves the deadlines of tickets that are still running.
    [Fact]
    public async Task A_new_holiday_moves_a_running_tickets_deadline_and_removing_it_moves_it_back()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var admin = await factory.ClientFor(Admin);
        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = $"Calendar {Guid.NewGuid():N}", message = "Please help.", priority = "High" });
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;
        var original = (await Waiting.Eventually(() => agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, t => t!.Sla is not null))!.Sla!;

        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
        var dueDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(original.ResponseDueUtc, manila)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            (await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = dueDay, name = "Test holiday" })).EnsureSuccessStatusCode();

            var moved = (await Waiting.Eventually(() => agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, t => t!.Sla!.ResponseDueUtc != original.ResponseDueUtc))!.Sla!;
            moved.ResponseDueUtc.Should().BeAfter(original.ResponseDueUtc, "the day it was due is now closed");
        }
        finally
        {
            await admin.DeleteAsync($"/api/sla/calendar/holidays/{dueDay}");
        }

        var back = (await Waiting.Eventually(() => agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, t => t!.Sla!.ResponseDueUtc == original.ResponseDueUtc))!.Sla!;
        back.ResolveDueUtc.Should().Be(original.ResolveDueUtc);
    }
```

```csharp
// tests/Yasda.Integration.Tests/SlaPolicyTests.cs (added)
    // Decision F10: an edited policy moves the deadlines of tickets still running too, not only those filed after it.
    [Fact]
    public async Task A_changed_policy_moves_the_running_tickets_deadlines_too()
    {
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var before = await LowOf(lead);
        var older = await FileLow(requester);
        var olderBefore = await ReadDeadlines(lead, older);
        try
        {
            await SetLow(lead, 600, 3000);
            var newer = await FileLow(requester);

            var olderNow = await Waiting.Eventually(() => lead.GetFromJsonAsync<TicketView>($"/api/tickets/{older}")!,
                t => t!.Sla!.ResponseDueUtc != olderBefore.Sla!.ResponseDueUtc);
            var newerTicket = await ReadDeadlines(lead, newer);

            // 600 and 3000 open minutes: the same rule for the old ticket and the new one.
            olderNow!.Sla!.ResponseDueUtc.Should().Be(DueAfter(olderNow.CreatedUtc, TimeSpan.FromMinutes(600)));
            olderNow.Sla.ResolveDueUtc.Should().Be(DueAfter(olderNow.CreatedUtc, TimeSpan.FromMinutes(3000)));
            newerTicket.Sla!.ResponseDueUtc.Should().Be(DueAfter(newerTicket.CreatedUtc, TimeSpan.FromMinutes(600)));
            newerTicket.Sla.ResolveDueUtc.Should().Be(DueAfter(newerTicket.CreatedUtc, TimeSpan.FromMinutes(3000)));
        }
        finally
        {
            await SetLow(lead, before.RespondMinutes, before.ResolveMinutes);
        }
    }
```

### Make each one fail once

In `ClockRecalculator.RecalculateAsync`, delete the `foreach` body and run the integration tests: both tests above fail, because a holiday and a policy save no longer move the ticket. Put it back. In `SlaClock.SetResponseTarget`, change the at-risk line to `calendar.Add(StartedUtc, within)` (the whole target, not three quarters): `The_at_risk_point_is_three_quarters_through_the_open_time_not_the_wall_clock_time` fails. Put it back.

## 5. Waiting on the requester stops the clock

Now the second half. When an agent asks the requester a question and sets the ticket to Pending, the team can do nothing until the answer comes. The clock should stop, and start again when the requester replies.

Sla learns about it the way it learns about everything: Tickets raises events and the outbox delivers them. Two new ones. First the public facts, in the Contracts project:


```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets.Contracts/TicketEvents.cs (added)
// The ticket is waiting on the requester, and its SLA clock stops until the requester answers (or the ticket moves on).
public sealed record TicketPending(long TicketId, DateTime PendingUtc) : IIntegrationEvent;

// The ticket is no longer waiting on the requester: its SLA clock runs again.
public sealed record TicketResumed(long TicketId, DateTime ResumedUtc) : IIntegrationEvent;
```

Then Tickets' own domain events. They are internal, in Tickets' words:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/TicketEvents.cs (added)
internal sealed record TicketPendingEvent(TicketId TicketId, DateTime PendingUtc) : IDomainEvent;

internal sealed record TicketResumedEvent(TicketId TicketId, DateTime ResumedUtc) : IDomainEvent;
```

The ticket raises them where its status moves. Setting it Pending raises one; the requester answering raises the other. And when a team lead moves a pending ticket so that it loses its owner, it goes back to New and stops waiting, so that raises "resumed" too:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/Ticket.cs (added)
    // Waiting on the requester: an agent asked something and can't go on without the answer.
    public void MarkPending(DateTime nowUtc)
    {
        EnsureIn("wait on the requester", TicketStatus.Open);
        Move(TicketStatus.Pending, nowUtc);
        Raise(new TicketPendingEvent(Id, nowUtc));
    }
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/Ticket.cs (added)
            // The requester answered: the ball is back with the agents.
            if (Status == TicketStatus.Pending)
            {
                Status = TicketStatus.Open;
                Raise(new TicketResumedEvent(Id, nowUtc));
            }
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/Ticket.cs (added)
            AssigneeId = null;
            AssigneeName = null;
            if (Status == TicketStatus.Pending) Raise(new TicketResumedEvent(Id, nowUtc));
            if (Status is TicketStatus.Open or TicketStatus.Pending) Status = TicketStatus.New;
        }
```

Each domain event becomes a public one, with the small publisher classes you know from lesson 13:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/Events/PublishTicketEvents.cs (added)
internal sealed class PublishTicketPending(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketPendingEvent>
{
    public async Task HandleAsync(TicketPendingEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketPending(domainEvent.TicketId.Value, domainEvent.PendingUtc), ct);
    }
}

internal sealed class PublishTicketResumed(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketResumedEvent>
{
    public async Task HandleAsync(TicketResumedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketResumed(domainEvent.TicketId.Value, domainEvent.ResumedUtc), ct);
    }
}
```

Register both in `AddTicketsModule`, next to the other publishers:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/TicketsModule.cs (added, in AddTicketsModule)
        services.AddScoped<IDomainEventHandler<TicketPendingEvent>, PublishTicketPending>();
        services.AddScoped<IDomainEventHandler<TicketResumedEvent>, PublishTicketResumed>();
```

Now Sla's side. The clock needs a way to pause and resume. Look at what it needs to remember: since when it has been paused, how much open time its finished pauses add up to, and the times of the last pause and resume, so that a message that arrives twice or out of order cannot confuse it:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Domain/SlaClock.cs (added)
    // While the ticket waits on the requester no time counts. PausedSinceUtc is set during a pause; PausedFor adds up the open
    // time of the pauses already over, and every due time is the start plus the target plus PausedFor.
    public DateTime? PausedSinceUtc { get; private set; }
    public TimeSpan PausedFor { get; private set; }
    public DateTime? LastPausedUtc { get; private set; }
    public DateTime? LastResumedUtc { get; private set; }
    public bool IsPaused => PausedSinceUtc is not null;
```

The two methods are short, and the guards are the whole point. `Pause` needs no calendar, because the paused time is added up when the pause ends. `Resume` adds the *open* time that passed (a pause over a weekend costs nothing for the weekend) and works the running targets out again:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Domain/SlaClock.cs (added)
    // The ticket now waits on the requester: the clock stops. A repeat, or a pause that was already followed by a resume, changes
    // nothing. Needs no calendar: the paused time is added up when the pause ends.
    public void Pause(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (!IsRunning) return;
        if (LastPausedUtc is not null && atUtc <= LastPausedUtc) return;

        LastPausedUtc = atUtc;
        if (LastResumedUtc is not null && atUtc <= LastResumedUtc) return;
        if (IsPaused) return;

        PausedSinceUtc = atUtc;
        Changed();
    }

    // The ticket no longer waits: the open time it was paused is added to every running target, and the due times are worked
    // out again.
    public void Resume(DateTime atUtc, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (LastResumedUtc is not null && atUtc <= LastResumedUtc) return;

        LastResumedUtc = atUtc;
        if (!IsPaused) return;
        if (atUtc < PausedSinceUtc) return;

        EndPause(atUtc, calendar);
        Changed();
    }
```

Every due time is now the start plus the target plus the paused time, so the two setters change by one term:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Domain/SlaClock.cs (added)
    private void SetResponseTarget(TimeSpan within, BusinessCalendar calendar)
    {
        RespondWithin = within;
        ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within + PausedFor));
        ResponseAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction + PausedFor));
    }

    private void SetResolveTarget(TimeSpan within, BusinessCalendar calendar)
    {
        ResolveWithin = within;
        ResolveDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within + PausedFor));
        ResolveAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction + PausedFor));
    }

    // Adds the open time of the pause that is ending, and works the running targets out again. The caller raises the event.
    private void EndPause(DateTime atUtc, BusinessCalendar calendar)
    {
        PausedFor = PausedFor + calendar.Between(PausedSinceUtc!.Value, atUtc);
        PausedSinceUtc = null;

        if (RespondedUtc is null) SetResponseTarget(RespondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(ResolveWithin, calendar);
    }
```

Two existing methods meet a pause too. An agent's answer during a pause is measured against a due time that already includes the pause so far, and resolving a ticket ends its pause first, so a ticket resolved after a long wait is not marked late:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Domain/SlaClock.cs (added)
    // The first answer to the requester. Only the first counts. An answer given during a pause is measured against a due time that
    // already includes the pause so far.
    public void RecordResponse(DateTime atUtc, BusinessCalendar calendar)
    {
        if (Cancelled) return;
        if (RespondedUtc is not null) return;

        atUtc = WholeMicroseconds(atUtc);
        if (PausedSinceUtc is not null && atUtc > PausedSinceUtc)
        {
            var pausedSoFar = PausedFor + calendar.Between(PausedSinceUtc.Value, atUtc);
            ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, RespondWithin + pausedSoFar));
        }

        RespondedUtc = atUtc;
        Changed();
    }

    // The ticket was resolved. Resolving does not count as a response: only an agent's public reply stops the response target
    // (decision of 2026-11-20), so a ticket fixed with no reply keeps showing its unanswered target.
    // A resolution that happened before the last reopen is old news, and one already known is a repeat.
    // Resolving ends a pause: the time paused until now is added to the deadlines first, so it is measured fairly.
    public void RecordResolution(DateTime atUtc, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null) return;
        if (LastReopenedUtc is not null && atUtc < LastReopenedUtc) return;

        if (IsPaused) EndPause(atUtc, calendar);
        ResolvedUtc = atUtc;
        Changed();
    }
```

While paused, `NextDueUtc` and `AtRiskFromUtc` are `null`: nothing is due, so nothing can be breached. The event carries a new flag, `Paused`, so the rest of the system can say so:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla.Contracts/DeadlinesChanged.cs (added)
// A ticket's deadlines, as Sla computes them. Version counts the changes of one ticket's clock, so a receiver can
// ignore an older answer that arrives late. A null Met means the target is still running; NextDueUtc is null once
// nothing is running or the clock is paused. AtRiskFromUtc is when the next target enters its last quarter. Paused means the ticket
// is waiting on the requester and no time is counting.
public sealed record DeadlinesChanged(
    long TicketId, int Version,
    DateTime ResponseDueUtc, bool? ResponseMet,
    DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused) : IIntegrationEvent;
```

The handlers that hear the two events are two more small classes:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Features/Events/ClockEventHandlers.cs (added)
internal sealed class PauseClockOnTicketPending(SlaDbContext db) : IIntegrationEventHandler<TicketPending>
{
    public async Task HandleAsync(TicketPending integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Pause(integrationEvent.PendingUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class ResumeClockOnTicketResumed(SlaDbContext db) : IIntegrationEventHandler<TicketResumed>
{
    public async Task HandleAsync(TicketResumed integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.Resume(integrationEvent.ResumedUtc, calendar);
        await db.SaveChangesAsync(ct);
    }
}
```

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/SlaModule.cs (added, in AddSlaModule)
        services.AddScoped<IIntegrationEventHandler<TicketPending>, PauseClockOnTicketPending>();
        services.AddScoped<IIntegrationEventHandler<TicketResumed>, ResumeClockOnTicketResumed>();
```

The clock has new columns, so EF Core writes the migration:

```bash
dotnet ef migrations add AddPause --project src/Modules/Sla/Yasda.Modules.Sla --output-dir Infrastructure/Migrations
```

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/Migrations/<timestamp>_AddPause.cs (the file EF Core made; replace its content)
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yasda.Modules.Sla.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_paused_utc",
                schema: "sla",
                table: "sla_clock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_resumed_utc",
                schema: "sla",
                table: "sla_clock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "paused_for",
                schema: "sla",
                table: "sla_clock",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "paused_since_utc",
                schema: "sla",
                table: "sla_clock",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_paused_utc",
                schema: "sla",
                table: "sla_clock");

            migrationBuilder.DropColumn(
                name: "last_resumed_utc",
                schema: "sla",
                table: "sla_clock");

            migrationBuilder.DropColumn(
                name: "paused_for",
                schema: "sla",
                table: "sla_clock");

            migrationBuilder.DropColumn(
                name: "paused_since_utc",
                schema: "sla",
                table: "sla_clock");
        }
    }
}
```

One detail of the model: the paused time is stored in whole microseconds, as a `bigint`, because the minutes the targets use would lose up to a minute per pause:

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/SlaDbContext.cs (added)
            e.Ignore(x => x.IsPaused);
            // Open time paused is kept to the microsecond: the minutes the targets use would lose up to a minute per pause.
            e.Property(x => x.PausedFor).HasConversion<MicrosecondsConverter>();
            e.Ignore(x => x.DomainEvents);
```

```csharp
// src/Modules/Sla/Yasda.Modules.Sla/Infrastructure/SlaDbContext.cs (added)
internal sealed class MicrosecondsConverter() : ValueConverter<TimeSpan, long>(v => v.Ticks / 10, v => TimeSpan.FromTicks(v * 10));
```

## 6. Show "Paused"

Tickets keeps its own copy of a ticket's deadlines, so the queue can sort and mark them without asking Sla. The copy learns one more fact:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/TicketSla.cs
namespace Yasda.Modules.Tickets.Domain;

// Tickets' copy of one ticket's deadlines, kept so a queue can sort and mark them without asking Sla. It lives in its
// own table: saving it never touches the ticket's row, so it can't bump the ticket's row version and make an agent's
// save fail, and it raises no ticket event. Sla owns the facts; this only remembers the latest answer it was told.
internal sealed class TicketSla
{
    private TicketSla() { }

    public TicketId TicketId { get; private set; }
    public int Version { get; private set; }
    public DateTime ResponseDueUtc { get; private set; }
    public bool? ResponseMet { get; private set; }
    public DateTime ResolveDueUtc { get; private set; }
    public bool? ResolutionMet { get; private set; }
    public DateTime? NextDueUtc { get; private set; }
    public DateTime? AtRiskFromUtc { get; private set; }
    public bool Paused { get; private set; }

    public static TicketSla ForTicket(TicketId ticketId)
    {
        return new TicketSla { TicketId = ticketId };
    }

    // Deliveries can repeat and arrive out of order. Only a newer version is taken, and the answer says whether it was.
    public bool Apply(int version, DateTime responseDueUtc, bool? responseMet, DateTime resolveDueUtc, bool? resolutionMet,
        DateTime? nextDueUtc, DateTime? atRiskFromUtc, bool paused)
    {
        if (version <= Version) return false;

        Version = version;
        ResponseDueUtc = responseDueUtc;
        ResponseMet = responseMet;
        ResolveDueUtc = resolveDueUtc;
        ResolutionMet = resolutionMet;
        NextDueUtc = nextDueUtc;
        AtRiskFromUtc = atRiskFromUtc;
        Paused = paused;
        return true;
    }
}
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/Events/StoreDeadlines.cs
using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

// Sla computed new deadlines for a ticket: remember them, so the queues can show and sort them. Delivery repeats and
// reorders, so only a newer version is applied. The ticket's own row is never touched.
internal sealed class StoreDeadlines(TicketsDbContext db) : IIntegrationEventHandler<DeadlinesChanged>
{
    public async Task HandleAsync(DeadlinesChanged integrationEvent, CancellationToken ct)
    {
        var ticketId = new TicketId(integrationEvent.TicketId);
        var stored = await db.TicketSlas.SingleOrDefaultAsync(s => s.TicketId == ticketId, ct);
        if (stored is null)
        {
            stored = TicketSla.ForTicket(ticketId);
            db.TicketSlas.Add(stored);
        }

        var applied = stored.Apply(integrationEvent.Version, integrationEvent.ResponseDueUtc, integrationEvent.ResponseMet,
            integrationEvent.ResolveDueUtc, integrationEvent.ResolutionMet, integrationEvent.NextDueUtc, integrationEvent.AtRiskFromUtc, integrationEvent.Paused);
        if (!applied) return;

        await db.SaveChangesAsync(ct);
    }
}
```

A new column means a migration, and the two functions that read it (the queue and the ticket page) return one more column. A shipped function is never edited in a lesson, so each gets a `v4` file, and a migration drops the old and creates the new. The result of a function cannot change with `CREATE OR REPLACE`, which is why it drops first, exactly as lesson 18 did:

```bash
dotnet ef migrations add AddPausedToTicketSla --project src/Modules/Tickets/Yasda.Modules.Tickets --output-dir Infrastructure/Migrations
dotnet ef migrations add AddPausedReadFunctions --project src/Modules/Tickets/Yasda.Modules.Tickets --output-dir Infrastructure/Migrations
```

```sql
-- src/Modules/Tickets/Yasda.Modules.Tickets/Infrastructure/Sql/Functions/tickets.get_ticket.v4.sql
-- One ticket, or no row when it doesn't exist or p_requester_id says the caller may only see their own. v4 adds sla_paused: a
-- ticket waiting on the requester has its SLA clock stopped.
CREATE OR REPLACE FUNCTION tickets.get_ticket(p_id bigint, p_requester_id bigint)
RETURNS TABLE (id bigint, subject text, status text, priority text, requester_id bigint, requester_name text,
               assignee_id bigint, assignee_name text, created_utc timestamptz, updated_utc timestamptz,
               first_response_utc timestamptz, resolved_utc timestamptz, closed_utc timestamptz, cancel_reason text,
               response_due_utc timestamptz, response_met boolean, resolve_due_utc timestamptz, resolution_met boolean,
               next_due_utc timestamptz, at_risk_from_utc timestamptz, sla_paused boolean,
               category_id bigint, category_name text, team_id bigint, team_name text)
LANGUAGE sql STABLE
AS $$
    SELECT t.id, t.subject::text, t.status::text, t.priority::text, t.requester_id, t.requester_name::text,
           t.assignee_id, t.assignee_name::text, t.created_utc, t.updated_utc, t.first_response_utc, t.resolved_utc,
           t.closed_utc, t.cancel_reason::text,
           s.response_due_utc, s.response_met, s.resolve_due_utc, s.resolution_met, s.next_due_utc, s.at_risk_from_utc, s.paused,
           t.category_id, t.category_name::text, t.team_id, t.team_name::text
    FROM tickets.ticket t
    LEFT JOIN tickets.ticket_sla s ON s.ticket_id = t.id
    WHERE t.id = p_id AND (p_requester_id IS NULL OR t.requester_id = p_requester_id);
$$;
```

```sql
-- src/Modules/Tickets/Yasda.Modules.Tickets/Infrastructure/Sql/Functions/tickets.search_tickets.v4.sql
-- The queues and "my tickets". v4 adds sla_paused, so a queue can say "Paused" instead of showing a deadline that is not running.
CREATE OR REPLACE FUNCTION tickets.search_tickets(
    p_requester_id bigint, p_status text, p_priority text, p_assignee_id bigint, p_unassigned boolean, p_search text,
    p_team_ids bigint[], p_no_team boolean,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, subject text, status text, priority text, requester_id bigint, requester_name text,
               assignee_id bigint, assignee_name text, created_utc timestamptz, updated_utc timestamptz,
               next_due_utc timestamptz, at_risk_from_utc timestamptz, sla_paused boolean, team_name text, category_name text)
LANGUAGE sql STABLE
AS $$
    SELECT t.id, t.subject::text, t.status::text, t.priority::text, t.requester_id, t.requester_name::text,
           t.assignee_id, t.assignee_name::text, t.created_utc, t.updated_utc, s.next_due_utc, s.at_risk_from_utc, s.paused,
           t.team_name::text, t.category_name::text
    FROM tickets.ticket t
    LEFT JOIN tickets.ticket_sla s ON s.ticket_id = t.id
    WHERE (p_requester_id IS NULL OR t.requester_id = p_requester_id)
      AND (p_status IS NULL OR t.status = p_status)
      AND (p_priority IS NULL OR t.priority = p_priority)
      AND (p_assignee_id IS NULL OR t.assignee_id = p_assignee_id)
      AND (NOT p_unassigned OR t.assignee_id IS NULL)
      AND (p_search IS NULL OR t.subject ILIKE '%' || p_search || '%' OR t.requester_name ILIKE '%' || p_search || '%'
           OR t.id::text = p_search)
      AND (p_team_ids IS NULL OR t.team_id = ANY(p_team_ids))
      AND (NOT p_no_team OR t.team_id IS NULL)
    ORDER BY
        CASE WHEN p_sort = 'id' AND NOT p_desc THEN t.id END ASC,
        CASE WHEN p_sort = 'id' AND p_desc     THEN t.id END DESC,
        CASE WHEN p_sort = 'subject' AND NOT p_desc THEN t.subject END ASC,
        CASE WHEN p_sort = 'subject' AND p_desc     THEN t.subject END DESC,
        CASE WHEN p_sort = 'status' AND NOT p_desc THEN t.status END ASC,
        CASE WHEN p_sort = 'status' AND p_desc     THEN t.status END DESC,
        CASE WHEN p_sort = 'priority' AND NOT p_desc THEN tickets.priority_rank(t.priority) END ASC,
        CASE WHEN p_sort = 'priority' AND p_desc     THEN tickets.priority_rank(t.priority) END DESC,
        CASE WHEN p_sort = 'requesterName' AND NOT p_desc THEN t.requester_name END ASC,
        CASE WHEN p_sort = 'requesterName' AND p_desc     THEN t.requester_name END DESC,
        CASE WHEN p_sort = 'assigneeName' AND NOT p_desc THEN t.assignee_name END ASC,
        CASE WHEN p_sort = 'assigneeName' AND p_desc     THEN t.assignee_name END DESC,
        CASE WHEN p_sort = 'createdUtc' AND NOT p_desc THEN t.created_utc END ASC,
        CASE WHEN p_sort = 'createdUtc' AND p_desc     THEN t.created_utc END DESC,
        CASE WHEN p_sort = 'updatedUtc' AND NOT p_desc THEN t.updated_utc END ASC,
        -- The soonest deadline first, or last when descending; a ticket with no running deadline is always at the end.
        CASE WHEN p_sort = 'nextDue' AND NOT p_desc THEN s.next_due_utc END ASC NULLS LAST,
        CASE WHEN p_sort = 'nextDue' AND p_desc     THEN s.next_due_utc END DESC NULLS LAST,
        t.updated_utc DESC, t.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Infrastructure/Migrations/<timestamp>_AddPausedReadFunctions.cs (the file EF Core made; replace its content)
﻿using Microsoft.EntityFrameworkCore.Migrations;
using Yasda.Shared.BuildingBlocks;

#nullable disable

namespace Yasda.Modules.Tickets.Infrastructure.Migrations
{
    // The queue and the ticket page now say whether the SLA clock is paused. The result of a function can't change with
    // CREATE OR REPLACE, so the old ones are dropped and the v4 files create them again.
    public partial class AddPausedReadFunctions : Migration
    {
        private static string Sql(string file)
        {
            return EmbeddedSql.Read(typeof(AddPausedReadFunctions).Assembly, file);
        }

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION tickets.search_tickets(bigint, text, text, bigint, boolean, text, bigint[], boolean, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION tickets.get_ticket(bigint, bigint);");
            migrationBuilder.Sql(Sql("tickets.search_tickets.v4.sql"));
            migrationBuilder.Sql(Sql("tickets.get_ticket.v4.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.get_ticket(bigint, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.search_tickets(bigint, text, text, bigint, boolean, text, bigint[], boolean, text, boolean, integer, integer);");
            migrationBuilder.Sql(Sql("tickets.search_tickets.v3.sql"));
            migrationBuilder.Sql(Sql("tickets.get_ticket.v3.sql"));
        }
    }
}
```

The handlers' result types gain the flag. A requester never sees any of it, so the queue's handler clears it with the other deadline fields:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/GetTicket/GetTicketHandler.cs (added)
// The deadlines Sla worked out, for staff. A requester never gets this (it is null for them).
internal sealed record TicketSlaDto(DateTime ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused);
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/GetTicket/GetTicketHandler.cs (added)
// One row of tickets.get_ticket, exactly as the SQL function returns it.
internal sealed record TicketRow(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason,
    DateTime? ResponseDueUtc, bool? ResponseMet, DateTime? ResolveDueUtc, bool? ResolutionMet, DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool? SlaPaused,
    long? CategoryId, string? CategoryName, long? TeamId, string? TeamName);
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/GetTicket/GetTicketHandler.cs (added)
        // Only staff see the deadlines, and only once Sla has told Tickets about them.
        TicketSlaDto? sla = null;
        if (isStaff && row.ResponseDueUtc is not null && row.ResolveDueUtc is not null)
        {
            sla = new TicketSlaDto(row.ResponseDueUtc.Value, row.ResponseMet, row.ResolveDueUtc.Value, row.ResolutionMet,
                row.NextDueUtc, row.AtRiskFromUtc, row.SlaPaused == true);
        }
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/SearchTickets/SearchTicketsHandler.cs (added)
internal sealed record TicketSummaryDto(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool? SlaPaused,
    string? TeamName, string? CategoryName);
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/SearchTickets/SearchTicketsHandler.cs (changed, near the end of HandleAsync)
        if (!isStaff)
        {
            for (var i = 0; i < items.Count; i++)
                items[i] = items[i] with { NextDueUtc = null, AtRiskFromUtc = null, SlaPaused = null };
        }
```

The browser app has matching shapes. In `Models.cs`, the queue's row and the ticket page's deadlines get the flag:

```csharp
// src/Yasda.Web/Models/Models.cs (added)
public sealed record TicketSummary(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool? SlaPaused,
    string? TeamName, string? CategoryName);
```

```csharp
// src/Yasda.Web/Models/Models.cs (added)
public sealed record TicketSlaFacts(DateTime ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused);
```

The words live in one place, `Format.Sla`, which gets one more case and an optional argument, so existing callers keep working:

```csharp
// src/Yasda.Web/Shared/Format.cs (added)
    // An SLA deadline as DESIGN.md 5.2 says it: "Breached 40m ago", "1h 10m left" or "6h left", and which of the three it is
    // (the screens color the first two, and mark their rows). The server decides when a ticket is at risk (Sla's rule);
    // this only compares those two times with now. A ticket waiting on the requester says "Paused": no time is counting. No deadline running (a finished ticket) says nothing.
    public static SlaView Sla(DateTime? nextDueUtc, DateTime? atRiskFromUtc, DateTime nowUtc, bool paused = false)
    {
        if (paused) return new SlaView("Paused", "paused");
        if (nextDueUtc is null) return new SlaView("", "none");

        if (nowUtc > nextDueUtc)
        {
            var late = nowUtc - nextDueUtc.Value;
            if (late < TimeSpan.FromMinutes(1)) return new SlaView("Breached just now", "breached");
            return new SlaView($"Breached {Span(late)} ago", "breached");
        }

        var left = nextDueUtc.Value - nowUtc;
        var text = "Under 1m left";
        if (left >= TimeSpan.FromMinutes(1)) text = $"{Span(left)} left";

        if (atRiskFromUtc is not null && nowUtc >= atRiskFromUtc) return new SlaView(text, "risk");
        return new SlaView(text, "ok");
    }
```

The queue and the ticket page pass the flag. In `Tickets.razor`, the line that builds the deadline text becomes:

```razor
@* src/Yasda.Web/Pages/Tickets/Tickets.razor (changed, inside the row loop) *@
                    var sla = Format.Sla(t.NextDueUtc, t.AtRiskFromUtc, Time.GetUtcNow().UtcDateTime, t.SlaPaused == true);
```

And in `TicketPage.razor`, the SLA line shows when something is running *or* the clock is paused, and `CurrentSla` passes the flag:

```razor
@* src/Yasda.Web/Pages/Tickets/TicketPage.razor (changed) *@
            @if (IsStaff && _ticket.Sla is not null && (_ticket.Sla.NextDueUtc is not null || _ticket.Sla.Paused))
```

```csharp
// src/Yasda.Web/Pages/Tickets/TicketPage.razor (changed, in @code)
        return Format.Sla(_ticket.Sla.NextDueUtc, _ticket.Sla.AtRiskFromUtc, Time.GetUtcNow().UtcDateTime, _ticket.Sla.Paused);
```

## 7. Prove it: the pause

The clock's tests again, with the office week. Monday 09:00 Manila, 4 open hours to respond:

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (added)
    [Fact]
    public void Resuming_adds_the_open_time_it_was_paused_to_the_deadlines()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));
        clock.ClearEvents();

        // Paused Monday 10:00 to Tuesday 10:00: 7 open hours on Monday and 2 on Tuesday.
        clock.Resume(ManilaTime(11, 24, 10), OfficeWeek());

        clock.IsPaused.Should().BeFalse();
        clock.PausedFor.Should().Be(TimeSpan.FromHours(9));
        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 13), "4 open hours after 09:00 was 13:00 Monday; 9 paused hours later it is 13:00 Tuesday");
        var changed = OnlyEventOf(clock);
        changed.Paused.Should().BeFalse();
        changed.NextDueUtc.Should().Be(ManilaTime(11, 24, 13));
    }
```

```csharp
// tests/Yasda.Modules.Sla.UnitTests/SlaClockTests.cs (added)
    [Fact]
    public void Resolving_while_paused_ends_the_pause_and_counts_the_paused_time_so_the_resolution_is_on_time()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));

        // The resolve target is 24 open hours (due Wednesday 15:00 without a pause). Resolved Wednesday 16:00 after a long pause.
        clock.RecordResolution(ManilaTime(11, 25, 16), OfficeWeek());

        clock.IsPaused.Should().BeFalse();
        clock.ResolutionMet.Should().BeTrue();
    }
```

| Test | Why it matters |
|---|---|
| `Pausing_stops_the_clock_so_nothing_is_due_or_at_risk_and_the_event_says_paused` | A paused clock has no due time to breach |
| `A_pause_over_a_weekend_costs_nothing_for_the_closed_days` | Only open time is added |
| `A_repeated_pause_or_resume_changes_nothing` | The outbox delivers at least once |
| `A_resume_that_arrives_before_its_pause_leaves_the_clock_running` | Out of order: the last word wins |
| `Pausing_a_clock_with_nothing_left_running_or_a_cancelled_one_changes_nothing` | Nothing to stop |
| `A_response_given_while_paused_is_measured_against_a_due_time_that_includes_the_pause_so_far` | Answering is not resuming |
| `Changing_the_calendar_while_paused_keeps_the_clock_paused` | A recalculation does not end a wait |

Tickets' domain raises the events. It needs no database to test:

```csharp
// tests/Yasda.Modules.Tickets.UnitTests/TicketEventTests.cs (added)
    [Fact]
    public void Waiting_on_the_requester_raises_pending_and_their_answer_raises_resumed()
    {
        var ticket = OpenTicket();
        ticket.MarkPending(Now.AddMinutes(5));
        ticket.ClearEvents();

        ticket.Reply(Requester, "Rita Cruz", "Here you go.", isInternal: false, authorIsStaff: false, Now.AddMinutes(9));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketResumedEvent(Id, Now.AddMinutes(9)));
        ticket.Status.Should().Be(TicketStatus.Open);
    }
```

| Test | Why it matters |
|---|---|
| `Taking_and_closing_raise_nothing_waiting_raises_pending_and_resolving_raises_resolved` | The old "nothing raised" test, updated: waiting raises one now |
| `An_agent_reply_or_a_note_while_waiting_does_not_resume_the_clock` | Only the requester's answer, or leaving the wait, resumes it |

The words, in the browser app's own unit tests:

```csharp
// tests/Yasda.Web.UnitTests/FormatTests.cs (added)
    [Fact]
    public void A_paused_clock_says_paused_whatever_the_times_are()
    {
        var view = Format.Sla(NoonToday.AddHours(-3), NoonToday.AddHours(-5), NoonToday, paused: true);

        view.State.Should().Be("paused");
        view.Text.Should().Be("Paused");
    }
```

And the whole chain: Tickets, the outbox, Sla, the outbox, Tickets' copy and the API's answer:

```csharp
// tests/Yasda.Integration.Tests/SlaTests.cs (added)
    // Waiting on the requester stops the clock (decision E4): nothing is due while paused, and the requester's answer starts it again.
    [Fact]
    public async Task A_ticket_waiting_on_the_requester_pauses_its_clock_and_their_answer_resumes_it()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");
        await ReadUntil(agent, id, HasDeadlines);
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        (await agent.PostAsync($"/api/tickets/{id}/pending", null)).EnsureSuccessStatusCode();
        var paused = await ReadUntil(agent, id, t => t.Sla?.Paused == true);
        paused.Sla!.NextDueUtc.Should().BeNull("a paused clock has nothing due");
        paused.Sla.AtRiskFromUtc.Should().BeNull();
        var queue = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={id}&pageSize=100");
        queue!.Items.Single(t => t.Id == id).SlaPaused.Should().BeTrue();

        (await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Here is the answer." })).EnsureSuccessStatusCode();
        var running = await ReadUntil(agent, id, t => t.Sla?.Paused == false);
        running.Sla!.NextDueUtc.Should().NotBeNull("the clock runs again");
        running.Sla.ResponseDueUtc.Should().BeOnOrAfter(paused.Sla.ResponseDueUtc, "the time spent waiting is not held against the team");
    }
```

The outbox test that round-trips every event through JSON needs the two new events in its list. It fails with a message telling you so, which is the point of that test:

```csharp
// tests/Yasda.Integration.Tests/OutboxTests.cs (added, in the list of events)
            new TicketsDomain.TicketPendingEvent(ticketsId, at),
            new TicketsDomain.TicketResumedEvent(ticketsId, at),
```

```csharp
// tests/Yasda.Integration.Tests/OutboxTests.cs (added, in the set of tested types)
            typeof(TicketsDomain.TicketPendingEvent), typeof(TicketsDomain.TicketResumedEvent),
```

### Make each one fail once

In `SlaClock.Resume`, delete the line `EndPause(atUtc, calendar);`. `Resuming_adds_the_open_time_it_was_paused_to_the_deadlines` fails (the due time did not move), and so does the integration test. Put it back. Then in `Pause`, delete the line `if (IsPaused) return;`: `A_repeated_pause_or_resume_changes_nothing` or its neighbors fail. Put it back.

## How a request flows

An agent waits on a requester, who answers on Tuesday morning:

```mermaid
sequenceDiagram
  participant A as Agent
  participant T as Tickets
  participant O as Outbox
  participant S as Sla clock
  participant Q as Tickets' copy
  A->>T: POST /api/tickets/42/pending
  T->>T: Status = Pending, raises TicketPendingEvent
  T->>O: saved with the ticket
  O->>S: TicketPending (after commit)
  S->>S: Pause: PausedSinceUtc = Monday 10:00
  S->>O: DeadlinesChanged (Paused = true)
  O->>Q: stored; the queue says "Paused"
  Note over T,S: Tuesday 10:00, the requester replies
  T->>O: TicketResumedEvent
  O->>S: TicketResumed
  S->>S: Resume: add the open time paused, recompute due times
  S->>O: DeadlinesChanged (Paused = false, new due times)
```

## Mistakes to avoid

- Subtracting the paused time when you show a deadline. Every screen and report must do it the same way; let the clock own it.
- Adding the wall-clock time of a pause. A pause over a weekend would give the team two extra days.
- Resuming on any reply. An agent's reminder to a waiting requester is not an answer.
- Forgetting that messages repeat and arrive out of order. A pause and a resume that cross on the way must leave the clock running.
- Leaving a paused clock with a due time. Nothing is due while no time counts, so nothing can be breached.
- Adding a non-null column without saying what old rows get.

## Let's run it!

```bash
dotnet build Yasda.slnx
dotnet test --solution Yasda.slnx
dotnet run --project src/Yasda.Api
```

The tests should end with no failures; the browser tests skip until you give them an address. Then try it by hand. Sign in as `requester@yasda.local` and file a ticket. Sign in as `agent@yasda.local`, take it, and press "Wait on the requester". The queue and the ticket page now say "Paused" where a deadline was. Sign back in as the requester and reply: the deadline is back, a little later than it was. Finally, as `admin@yasda.local`, open "Business hours" and add tomorrow's date as a holiday: the deadline of every ticket that was due tomorrow moves to the day after.

## Let's check!

1. Why is the at-risk point a stored column, three quarters of the way through the open time, and not "the due time minus a quarter of the target"?
2. Why does `Recalculate` leave a finished target alone?
3. Why are the new calendar and the new due times saved in the same `SaveChangesAsync`?
4. What does a policy change now do to a ticket that is already running, and what did it do in lesson 14?
5. Why does `Pause` need no calendar, while `Resume` does?
6. A ticket is paused over a weekend. How much does the pause add to the deadline, and why?
7. Why must a resolution during a pause end the pause first?
8. Why are `NextDueUtc` and `AtRiskFromUtc` `null` while paused?
9. Why does the migration for the at-risk columns say what the old rows get?

And that's where this lesson ends. A deadline now means what the team's promise means: open hours, minus the time the ticket waited for the requester, recomputed whenever the rules change. Next time, we make those deadlines matter: a scheduler that warns before a breach and announces a breach once, even across a restart. Good luck, developer!

---

← [Lesson 23 — Holidays and the business hours screen](23-holidays.md) · Next: [Lesson 25 — Breaches: the scheduler that warns and escalates](25-breaches.md) →
