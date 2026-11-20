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
    public DbSet<TicketSla> TicketSlas => Set<TicketSla>();
    public DbSet<Category> Categories => Set<Category>();

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
        if (ticket is null) return null;
        if (!ticket.IsVisibleTo(user, userIsStaff)) return null;
        return ticket;
    }

    // The events a ticket raises are written to the outbox before PostgreSQL would hand out an identity value, so the
    // number is taken first and given to Ticket.Create.
    public async Task<TicketId> NextTicketIdAsync(CancellationToken ct)
    {
        var next = await Database.SqlQuery<long>($"SELECT nextval('tickets.ticket_id_seq') AS \"Value\"").SingleAsync(ct);
        return new TicketId(next);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        c.Properties<TeamId>().HaveConversion<TeamIdConverter>();
        // Statuses and priorities are stored by name: readable in SQL, and safe if the enum is ever reordered.
        c.Properties<TicketStatus>().HaveConversion<string>().HaveMaxLength(20);
        c.Properties<TicketPriority>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("ticket_id_seq");

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.TraceParent).HasMaxLength(55);   // a W3C traceparent is always 55 characters
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<TicketSla>(e =>
        {
            e.ToTable("ticket_sla");
            // No foreign key to ticket: the row arrives later, from Sla, and may even arrive first.
            e.HasKey(x => x.TicketId);
            e.Property(x => x.TicketId).ValueGeneratedNever();
            // Two deliveries at once must not let an older answer overwrite a newer one.
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("category");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(Category.NameMaxLength);
            e.Property(x => x.TeamName).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.TeamId);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<Ticket>(e =>
        {
            e.ToTable("ticket", t =>
            {
                t.HasCheckConstraint("ck_ticket_status", "status IN ('New', 'Open', 'Pending', 'Resolved', 'Closed', 'Cancelled')");
                t.HasCheckConstraint("ck_ticket_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
            });
            e.HasKey(x => x.Id);
            // Not an identity column: the id is taken from the sequence before the ticket is saved (see NextTicketIdAsync).
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Subject).HasMaxLength(Ticket.SubjectMaxLength);
            e.Property(x => x.RequesterName).HasMaxLength(100);
            e.Property(x => x.AssigneeName).HasMaxLength(100);
            e.Property(x => x.CategoryName).HasMaxLength(100);
            e.Property(x => x.TeamName).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(Ticket.ReasonMaxLength);
            e.HasIndex(x => new { x.Status, x.UpdatedUtc });
            e.HasIndex(x => x.RequesterId);
            e.HasIndex(x => x.AssigneeId);
            e.HasIndex(x => x.TeamId);
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
}

internal sealed class TicketIdConverter() : ValueConverter<TicketId, long>(v => v.Value, v => new TicketId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));
internal sealed class CategoryIdConverter() : ValueConverter<CategoryId, long>(v => v.Value, v => new CategoryId(v));
internal sealed class TeamIdConverter() : ValueConverter<TeamId, long>(v => v.Value, v => new TeamId(v));

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
