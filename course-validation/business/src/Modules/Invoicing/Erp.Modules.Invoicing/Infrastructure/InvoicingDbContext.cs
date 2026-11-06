using System.Data.Common;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Erp.Modules.Invoicing.Infrastructure;

// One row per numbered document type. Updating it inside the invoice's transaction is what makes numbers gap-free.
internal sealed class DocumentCounter
{
    public string Name { get; private set; } = default!;
    public long NextValue { get; private set; }
}

internal sealed class InvoicingDbContext(DbContextOptions<InvoicingDbContext> options) : DbContext(options)
{
    public const string Schema = "invoicing";

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<WithholdingCertificate> WithholdingCertificates => Set<WithholdingCertificate>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    public Task<CompanyProfile> CompanyProfileAsync(CancellationToken ct) =>
        CompanyProfiles.SingleAsync(p => p.Id == CompanyProfile.SingletonId, ct);

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    // A sequence would be faster, but a rolled-back transaction burns its number, and invoice numbers must have no
    // gaps. This row stays locked until the transaction ends, so invoices are numbered one at a time.
    public async Task<string> AllocateInvoiceNumberAsync(DateTime nowUtc, CancellationToken ct)
    {
        var value = (await Database.SqlQuery<long>(
                $"UPDATE invoicing.document_counter SET next_value = next_value + 1 WHERE name = 'invoice' RETURNING next_value - 1 AS \"Value\"")
            .ToListAsync(ct)).Single();
        return DocumentNumber.Format("SI", value, nowUtc);
    }

    // Counts every printing atomically; the BIR requires "REPRINT" on any copy after the first (RR 7-2024 Sec. 6(B.21)).
    public async Task<int?> RecordPrintAsync(long invoiceId, CancellationToken ct) =>
        (await Database.SqlQuery<int>(
                $"UPDATE invoicing.invoice SET print_count = print_count + 1 WHERE id = {invoiceId} RETURNING print_count AS \"Value\"")
            .ToListAsync(ct)).Cast<int?>().SingleOrDefault();

    // The payment id travels in PaymentRecorded, so it must exist before SaveChanges writes the event.
    public async Task<PaymentId> NextPaymentIdAsync(CancellationToken ct) =>
        new((await Database.SqlQuery<long>($"SELECT nextval('invoicing.payment_id_seq') AS \"Value\"").ToListAsync(ct)).Single());

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<InvoiceId>().HaveConversion<InvoiceIdConverter>();
        c.Properties<PaymentId>().HaveConversion<PaymentIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<WithholdingCertificateId>().HaveConversion<WithholdingCertificateIdConverter>();
        c.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        c.Properties<Quantity>().HaveConversion<QuantityConverter>().HavePrecision(18, 3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("payment_id_seq");

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<DocumentCounter>(e =>
        {
            e.ToTable("document_counter");
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(30);
            e.HasData(new { Name = "invoice", NextValue = 1L });
        });

        b.Entity<Invoice>(e =>
        {
            e.ToTable("invoice", t =>
            {
                t.HasCheckConstraint("ck_invoice_status", "status BETWEEN 0 AND 3");
                t.HasCheckConstraint("ck_invoice_paid", "amount_paid >= 0 AND amount_paid <= total");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique();
            // One live invoice per shipment; a void one can be replaced.
            e.HasIndex(x => x.ShipmentId).IsUnique().HasFilter("status <> 3");
            e.Property(x => x.OrderNumber).HasMaxLength(30);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerTin).HasMaxLength(20);
            e.Property(x => x.CustomerAddress).HasMaxLength(500);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.VatRate).HasPrecision(5, 4);
            e.OwnsOne(x => x.Seller, s =>
            {
                s.Property(x => x.Name).HasMaxLength(200);
                s.Property(x => x.TradeName).HasMaxLength(200);
                s.Property(x => x.Tin).HasMaxLength(20);
                s.Property(x => x.Address).HasMaxLength(500);
                s.Property(x => x.InvoiceTitle).HasMaxLength(50);
                s.Property(x => x.PermitNumber).HasMaxLength(100);
                s.Property(x => x.SeriesRange).HasMaxLength(100);
                s.Property(x => x.PermitDateIssued).HasMaxLength(10);
            });
            e.Navigation(x => x.Seller).IsRequired();
            e.OwnsOne(x => x.SpecialDiscount, d =>
            {
                d.Property(x => x.Program).HasMaxLength(50);
                d.Property(x => x.IdLabel).HasMaxLength(50);
                d.Property(x => x.HolderName).HasMaxLength(200);
                d.Property(x => x.IdNumber).HasMaxLength(50);
                d.Property(x => x.Rate).HasPrecision(5, 4);
                d.Property(x => x.VatRateRemoved).HasPrecision(5, 4);
            });
            e.HasIndex(x => new { x.CustomerId, x.Status });
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.BalanceDue);
            e.Ignore(x => x.DomainEvents);
            e.Property<int>("PrintCount");

            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("invoice_line");
                l.WithOwner().HasForeignKey("InvoiceId");
                l.Property<long>("Id");
                l.HasKey("Id");
                l.Property(x => x.Description).HasMaxLength(200);
                l.Property(x => x.Unit).HasMaxLength(10);
                l.Ignore(x => x.LineTotal);
            });
            e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

            e.OwnsMany(x => x.Payments, p =>
            {
                p.ToTable("payment", t => t.HasCheckConstraint("ck_payment_amount", "amount >= 0 AND tax_withheld >= 0 AND amount + tax_withheld > 0"));
                p.WithOwner().HasForeignKey("InvoiceId");
                p.HasKey(x => x.Id);
                p.Property(x => x.Id).ValueGeneratedNever();
                p.Property(x => x.Reference).HasMaxLength(100);
                p.Ignore(x => x.Settled);
                p.Property(x => x.VoidReason).HasMaxLength(500);
                p.Ignore(x => x.IsVoid);
            });
            e.Navigation(x => x.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<CompanyProfile>(e =>
        {
            e.ToTable("company_profile", t => t.HasCheckConstraint("ck_company_profile_single_row", "id = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RegisteredName).HasMaxLength(200);
            e.Property(x => x.TradeName).HasMaxLength(200);
            e.Property(x => x.Tin).HasMaxLength(20);
            e.Property(x => x.RegisteredAddress).HasMaxLength(500);
            e.Property(x => x.VatRate).HasPrecision(5, 4);
            e.Property(x => x.InvoiceTitle).HasMaxLength(50);
            e.Property(x => x.PermitNumber).HasMaxLength(100);
            e.Property(x => x.SeriesRange).HasMaxLength(100);
            e.Property(x => x.PermitDateIssued).HasMaxLength(10);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.Tax);
            e.Ignore(x => x.IsComplete);
            // Every database starts with the one row, blank; the business fills it in on its first day.
            e.HasData(new
            {
                Id = CompanyProfile.SingletonId, RegisteredName = "", Tin = "", RegisteredAddress = "",
                TaxRegime = TaxRegime.Vat, VatRate = 0.12m, InvoiceTitle = "SALES INVOICE"
            });
        });

        b.Entity<WithholdingCertificate>(e =>
        {
            e.ToTable("withholding_certificate", t =>
            {
                t.HasCheckConstraint("ck_withholding_certificate_quarter", "quarter BETWEEN 1 AND 4");
                t.HasCheckConstraint("ck_withholding_certificate_amount", "amount > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.HasIndex(x => new { x.Year, x.Quarter, x.CustomerId });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // A payment-only change still has to check the invoice's xmin, or two clerks could both record the last peso.
        if (ChangeTracker.Entries<Payment>().Any(p => p.State != EntityState.Unchanged))
            foreach (var invoice in ChangeTracker.Entries<Invoice>().Where(i => i.State == EntityState.Unchanged))
                invoice.Property(i => i.Status).IsModified = true;

        return base.SaveChangesAsync(ct);
    }
}

internal sealed class InvoiceIdConverter() : ValueConverter<InvoiceId, long>(v => v.Value, v => new InvoiceId(v));
internal sealed class PaymentIdConverter() : ValueConverter<PaymentId, long>(v => v.Value, v => new PaymentId(v));
internal sealed class WithholdingCertificateIdConverter()
    : ValueConverter<WithholdingCertificateId, long>(v => v.Value, v => new WithholdingCertificateId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(v => v.Amount, v => new Money(v));
internal sealed class QuantityConverter() : ValueConverter<Quantity, decimal>(v => v.Value, v => new Quantity(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class InvoicingDbContextFactory : IDesignTimeDbContextFactory<InvoicingDbContext>
{
    public InvoicingDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<InvoicingDbContext>();
        InvoicingDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new InvoicingDbContext(o.Options);
    }
}
