using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Infrastructure;

internal sealed class InvoicingDatabase : IModuleDatabase
{
    public string Module => "Invoicing";
    public string Schema => InvoicingDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<InvoicingDbContext>();
        InvoicingDbContext.Configure(options, ownerConnection);
        await using var db = new InvoicingDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
    }

    // Invoices only ever come from shipments; the seed fills in a demo company so they can be issued.
    public Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(ownerConnection, EmbeddedSql.Read(typeof(InvoicingDatabase).Assembly, "DevSeed.sql"), ct);
}
