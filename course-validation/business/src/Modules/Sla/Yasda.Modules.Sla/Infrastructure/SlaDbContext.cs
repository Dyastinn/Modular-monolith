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

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, DbConnection connection)
    {
        return options
            .UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
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

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
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
            e.Ignore(x => x.DomainEvents);
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
