using System.Data.Common;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Yasda.Modules.Sla.Infrastructure;

internal sealed class SlaDbContext(DbContextOptions<SlaDbContext> options) : DbContext(options)
{
    public const string Schema = "sla";

    public DbSet<SlaClock> Clocks => Set<SlaClock>();
    public DbSet<SlaPolicy> Policies => Set<SlaPolicy>();
    public DbSet<WorkCalendar> Calendars => Set<WorkCalendar>();
    public DbSet<CategoryPolicy> CategoryPolicies => Set<CategoryPolicy>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, DbConnection connection)
    {
        return options
            .UseNpgsql(connection, npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
                // The calendar loads two lists (days and holidays); separate queries keep it from loading their product.
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    }

    // Every event after the first one needs the ticket's clock. When it isn't there yet, the answer is an exception: the
    // outbox delivers the message again later, and by then the event that starts the clock has usually been handled.
    public async Task<SlaClock> GetClockAsync(long ticketId, CancellationToken ct)
    {
        var id = new TicketId(ticketId);
        var clock = await Clocks.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (clock is null) throw new InvalidOperationException($"No SLA clock for ticket #{ticketId} yet");
        return clock;
    }

    // The numbers a ticket's clock uses: its category's own policy for that priority if there is one, otherwise the installation's.
    public async Task<(TimeSpan Respond, TimeSpan Resolve)> TargetsForAsync(Priority priority, CategoryId? categoryId, CancellationToken ct)
    {
        if (categoryId is not null)
        {
            var id = categoryId.Value;
            var own = await CategoryPolicies.SingleOrDefaultAsync(p => p.CategoryId == id && p.Priority == priority, ct);
            if (own is not null) return (own.RespondWithin, own.ResolveWithin);
        }

        var policy = await Policies.SingleAsync(p => p.Priority == priority, ct);
        return (policy.RespondWithin, policy.ResolveWithin);
    }

    // The organization's calendar as the pure class the deadline math runs on.
    public async Task<BusinessCalendar> GetBusinessCalendarAsync(CancellationToken ct)
    {
        var calendar = await Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);
        return calendar.ToBusinessCalendar();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<CalendarId>().HaveConversion<CalendarIdConverter>();
        c.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        // Weekdays are stored by name, like priorities.
        c.Properties<DayOfWeek>().HaveConversion<string>().HaveMaxLength(10);
        // Priorities are stored by name: readable in SQL, and safe if the enum is ever reordered.
        c.Properties<Priority>().HaveConversion<string>().HaveMaxLength(20);
        // Targets are stored as whole minutes, the unit a person types.
        c.Properties<TimeSpan>().HaveConversion<MinutesConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.TraceParent).HasMaxLength(55);   // a W3C traceparent is always 55 characters
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<SlaClock>(e =>
        {
            e.ToTable("sla_clock", t =>
            {
                t.HasCheckConstraint("ck_sla_clock_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ticket_id").ValueGeneratedNever();
            e.Property(x => x.RespondWithin).HasColumnName("respond_minutes");
            e.Property(x => x.ResolveWithin).HasColumnName("resolve_minutes");
            // Two deliveries for one ticket at once: the second save fails, and the outbox delivers it again.
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            // These are worked out from the stored facts; they are not columns.
            e.Ignore(x => x.ResponseMet);
            e.Ignore(x => x.ResolutionMet);
            e.Ignore(x => x.NextDueUtc);
            e.Ignore(x => x.AtRiskFromUtc);
            e.Ignore(x => x.IsRunning);
            e.Ignore(x => x.IsPaused);
            // The scheduler finds the clocks whose check time has come: only running clocks have one.
            e.HasIndex(x => x.NextCheckUtc).HasFilter("next_check_utc IS NOT NULL");
            // Open time paused is kept to the microsecond: the minutes the targets use would lose up to a minute per pause.
            e.Property(x => x.PausedFor).HasConversion<MicrosecondsConverter>();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<CategoryPolicy>(e =>
        {
            e.ToTable("category_policy", t =>
            {
                t.HasCheckConstraint("ck_category_policy_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
                t.HasCheckConstraint("ck_category_policy_targets", "respond_minutes > 0 AND resolve_minutes >= respond_minutes");
            });
            // No foreign key to the category: it belongs to Tickets.
            e.HasKey(x => new { x.CategoryId, x.Priority });
            e.Property(x => x.RespondWithin).HasColumnName("respond_minutes");
            e.Property(x => x.ResolveWithin).HasColumnName("resolve_minutes");
            e.Property(x => x.UpdatedByName).HasMaxLength(100);
        });

        b.Entity<SlaPolicy>(e =>
        {
            e.ToTable("sla_policy", t =>
            {
                t.HasCheckConstraint("ck_sla_policy_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
                t.HasCheckConstraint("ck_sla_policy_targets", "respond_minutes > 0 AND resolve_minutes >= respond_minutes");
            });
            e.HasKey(x => x.Priority);
            e.Property(x => x.Priority).ValueGeneratedNever();
            e.Property(x => x.RespondWithin).HasColumnName("respond_minutes");
            e.Property(x => x.ResolveWithin).HasColumnName("resolve_minutes");
            e.Property(x => x.UpdatedByName).HasMaxLength(100);

            // Every installation starts with these (decision D3, 2026-11-20). An admin or team lead edits them on a screen.
            var installed = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                SeededPolicy(Priority.Critical, hours: 1, resolveHours: 4, installed),
                SeededPolicy(Priority.High, hours: 4, resolveHours: 24, installed),
                SeededPolicy(Priority.Normal, hours: 8, resolveHours: 72, installed),
                SeededPolicy(Priority.Low, hours: 24, resolveHours: 120, installed));
        });

        ConfigureCalendar(b);
    }

    private static void ConfigureCalendar(ModelBuilder b)
    {
        var calendarId = new CalendarId(1);
        var installed = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

        b.Entity<WorkCalendar>(e =>
        {
            e.ToTable("work_calendar");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.UpdatedByName).HasMaxLength(100);
            // Two admins saving at once: the second save fails instead of overwriting the first.
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.DomainEvents);

            e.HasMany(x => x.Days).WithOne().HasForeignKey("CalendarId");
            e.Navigation(x => x.Days).HasField("_days").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Holidays).WithOne().HasForeignKey("CalendarId");
            e.Navigation(x => x.Holidays).HasField("_holidays").UsePropertyAccessMode(PropertyAccessMode.Field);

            // Every installation starts Monday to Friday, 08:00 to 17:00, in Manila (decision E3). An admin edits it on a screen.
            e.HasData(new
            {
                Id = calendarId,
                TimeZoneId = "Asia/Manila",
                UpdatedUtc = installed,
                UpdatedById = new UserId(0),
                UpdatedByName = "Installation default",
            });
        });

        b.Entity<WorkDay>(e =>
        {
            e.ToTable("work_day", t => t.HasCheckConstraint("ck_work_day_hours", "(opens IS NULL AND closes IS NULL) OR closes > opens"));
            e.HasKey("CalendarId", nameof(WorkDay.Day));
            e.Ignore(x => x.IsOpen);
            e.HasData(
                SeededDay(calendarId, DayOfWeek.Monday, open: true),
                SeededDay(calendarId, DayOfWeek.Tuesday, open: true),
                SeededDay(calendarId, DayOfWeek.Wednesday, open: true),
                SeededDay(calendarId, DayOfWeek.Thursday, open: true),
                SeededDay(calendarId, DayOfWeek.Friday, open: true),
                SeededDay(calendarId, DayOfWeek.Saturday, open: false),
                SeededDay(calendarId, DayOfWeek.Sunday, open: false));
        });

        b.Entity<Holiday>(e =>
        {
            e.ToTable("holiday");
            e.HasKey("CalendarId", nameof(Holiday.Date));
            e.Property(x => x.Name).HasMaxLength(WorkCalendar.HolidayNameMaxLength);
        });
    }

    private static object SeededDay(CalendarId calendarId, DayOfWeek day, bool open)
    {
        if (!open) return new { CalendarId = calendarId, Day = day, Opens = (TimeOnly?)null, Closes = (TimeOnly?)null };
        return new { CalendarId = calendarId, Day = day, Opens = (TimeOnly?)new TimeOnly(8, 0), Closes = (TimeOnly?)new TimeOnly(17, 0) };
    }

    // HasData takes the property values, because the policy's setters are private.
    private static object SeededPolicy(Priority priority, int hours, int resolveHours, DateTime installed)
    {
        return new
        {
            Priority = priority,
            RespondWithin = TimeSpan.FromHours(hours),
            ResolveWithin = TimeSpan.FromHours(resolveHours),
            UpdatedUtc = installed,
            UpdatedById = new UserId(0),
            UpdatedByName = "Installation default",
        };
    }
}

internal sealed class TicketIdConverter() : ValueConverter<TicketId, long>(v => v.Value, v => new TicketId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));
internal sealed class CategoryIdConverter() : ValueConverter<CategoryId, long>(v => v.Value, v => new CategoryId(v));
internal sealed class CalendarIdConverter() : ValueConverter<CalendarId, int>(v => v.Value, v => new CalendarId(v));
internal sealed class MicrosecondsConverter() : ValueConverter<TimeSpan, long>(v => v.Ticks / 10, v => TimeSpan.FromTicks(v * 10));
internal sealed class MinutesConverter() : ValueConverter<TimeSpan, int>(v => (int)v.TotalMinutes, v => TimeSpan.FromMinutes(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class SlaDbContextFactory : IDesignTimeDbContextFactory<SlaDbContext>
{
    public SlaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SlaDbContext>();
        SlaDbContext.Configure(options, new Npgsql.NpgsqlConnection("Host=localhost;Database=yasda_design_time"));
        return new SlaDbContext(options.Options);
    }
}
