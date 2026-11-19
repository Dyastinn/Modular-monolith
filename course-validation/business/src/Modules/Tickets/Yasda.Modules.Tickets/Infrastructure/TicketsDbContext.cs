using System.Data.Common;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)
{
    public const string Schema = "tickets";

    public DbSet<Ticket> Tickets => Set<Ticket>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    // null when there is no such ticket, and when the caller may not see it: a requester can't even learn that
    // someone else's ticket exists.
    public async Task<Ticket?> GetVisibleTicketAsync(long ticketId, UserId user, bool userIsStaff, CancellationToken ct)
    {
        var id = new TicketId(ticketId);
        var ticket = await Tickets.SingleOrDefaultAsync(t => t.Id == id, ct);
        return ticket is not null && ticket.IsVisibleTo(user, userIsStaff) ? ticket : null;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        // Statuses and priorities are stored by name: readable in SQL, and safe if the enum is ever reordered.
        c.Properties<TicketStatus>().HaveConversion<string>().HaveMaxLength(20);
        c.Properties<TicketPriority>().HaveConversion<string>().HaveMaxLength(20);
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

        b.Entity<Ticket>(e =>
        {
            e.ToTable("ticket", t =>
            {
                t.HasCheckConstraint("ck_ticket_status", "status IN ('New', 'Open', 'Pending', 'Resolved', 'Closed', 'Cancelled')");
                t.HasCheckConstraint("ck_ticket_priority", "priority IN ('Low', 'Normal', 'High', 'Urgent')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Subject).HasMaxLength(Ticket.SubjectMaxLength);
            e.Property(x => x.RequesterName).HasMaxLength(100);
            e.Property(x => x.AssigneeName).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(Ticket.ReasonMaxLength);
            e.HasIndex(x => new { x.Status, x.UpdatedUtc });
            e.HasIndex(x => x.RequesterId);
            e.HasIndex(x => x.AssigneeId);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.IsFinished);
            e.Ignore(x => x.DomainEvents);

            e.OwnsMany(x => x.Messages, m =>
            {
                m.ToTable("ticket_message");
                m.WithOwner().HasForeignKey("TicketId");
                m.HasKey(x => x.Id);
                m.Property(x => x.Id).UseIdentityColumn();
                m.Property(x => x.AuthorName).HasMaxLength(100);
                m.Property(x => x.Body).HasMaxLength(Ticket.MessageMaxLength);
            });
            e.Navigation(x => x.Messages).HasField("_messages").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // A reply alone adds a message row and never updates the ticket row, so the ticket's xmin would never be
        // checked. Touch it, so two people replying to a ticket that just closed can't both get through.
        if (ChangeTracker.Entries<TicketMessage>().Any(m => m.State == EntityState.Added))
            foreach (var ticket in ChangeTracker.Entries<Ticket>().Where(t => t.State == EntityState.Unchanged))
                ticket.Property(t => t.UpdatedUtc).IsModified = true;

        return base.SaveChangesAsync(ct);
    }
}

internal sealed class TicketIdConverter() : ValueConverter<TicketId, long>(v => v.Value, v => new TicketId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class TicketsDbContextFactory : IDesignTimeDbContextFactory<TicketsDbContext>
{
    public TicketsDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<TicketsDbContext>();
        TicketsDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=yasda_design_time"));
        return new TicketsDbContext(o.Options);
    }
}
