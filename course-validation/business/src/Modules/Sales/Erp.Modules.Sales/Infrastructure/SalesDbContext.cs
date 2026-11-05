using System.Data.Common;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Erp.Modules.Sales.Infrastructure;

internal sealed class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
    public const string Schema = "sales";

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    public async Task<string> NextOrderNumberAsync(DateTime nowUtc, CancellationToken ct)
    {
        // ToList, not Single(): EF would wrap Single() in a subquery.
        var next = (await Database
            .SqlQuery<long>($"SELECT nextval('sales.sales_order_number_seq') AS \"Value\"")
            .ToListAsync(ct)).Single();
        return OrderNumbering.Format(next, nowUtc);
    }

    // Holds the customer's row until the transaction ends, so two submits for one customer can't both pass the
    // credit check on the same stale numbers. Must be called inside a transaction.
    public Task LockCustomerAsync(CustomerId id, CancellationToken ct) =>
        Database.ExecuteSqlAsync($"SELECT 1 FROM sales.customer WHERE id = {id.Value} FOR UPDATE", ct);

    // The customer's other submitted orders still waiting for a manager. They already count against credit.
    public async Task<Money> PendingTotalAsync(CustomerId customerId, SalesOrderId excluding, CancellationToken ct)
    {
        var pending = await SalesOrders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Status == SalesOrderStatus.PendingApproval && o.Id != excluding)
            .ToListAsync(ct);
        return pending.Aggregate(Money.Zero, (sum, o) => sum + o.Total);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<SalesOrderId>().HaveConversion<SalesOrderIdConverter>();
        c.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        c.Properties<ItemId>().HaveConversion<ItemIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        c.Properties<Quantity>().HaveConversion<QuantityConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("sales_order_number_seq");

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("customer", t =>
            {
                t.HasCheckConstraint("ck_customer_credit_limit", "credit_limit >= 0");
                t.HasCheckConstraint("ck_customer_balance", "outstanding_balance >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
        });

        b.Entity<ItemCategory>(e =>
        {
            e.ToTable("item_category", t => t.HasCheckConstraint("ck_item_category_max_qty", "max_quantity_per_order > 0"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.RequiredRole).HasMaxLength(50);
        });

        b.Entity<Item>(e =>
        {
            e.ToTable("item", t => t.HasCheckConstraint("ck_item_unit_price", "unit_price >= 0"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SalesOrder>(e =>
        {
            e.ToTable("sales_order", t => t.HasCheckConstraint("ck_sales_order_status", "status BETWEEN 0 AND 4"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.Property(x => x.OrderNumber).HasMaxLength(30);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.HasIndex(x => new { x.CustomerId, x.Status });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.Total);
            e.Ignore(x => x.RequiresApproval);
            e.Ignore(x => x.HoldsStock);
            e.Ignore(x => x.DomainEvents);

            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("sales_order_line", t =>
                {
                    t.HasCheckConstraint("ck_sales_order_line_qty", "quantity > 0");
                    t.HasCheckConstraint("ck_sales_order_line_price", "unit_price >= 0");
                });
                l.WithOwner().HasForeignKey("SalesOrderId");
                l.Property<long>("Id");
                l.HasKey("Id");
                l.Ignore(x => x.LineTotal);
            });
            e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // A lines-only edit never updates the order row, so its xmin would never be checked. Touch it.
        if (ChangeTracker.Entries<SalesOrderLine>().Any(l => l.State != EntityState.Unchanged))
            foreach (var order in ChangeTracker.Entries<SalesOrder>().Where(o => o.State == EntityState.Unchanged))
                order.Property(o => o.Status).IsModified = true;

        return base.SaveChangesAsync(ct);
    }
}

internal sealed class SalesOrderIdConverter() : ValueConverter<SalesOrderId, long>(v => v.Value, v => new SalesOrderId(v));
internal sealed class CustomerIdConverter() : ValueConverter<CustomerId, long>(v => v.Value, v => new CustomerId(v));
internal sealed class ItemIdConverter() : ValueConverter<ItemId, long>(v => v.Value, v => new ItemId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(v => v.Amount, v => new Money(v));
internal sealed class QuantityConverter() : ValueConverter<Quantity, int>(v => v.Value, v => new Quantity(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class SalesDbContextFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<SalesDbContext>();
        SalesDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new SalesDbContext(o.Options);
    }
}
