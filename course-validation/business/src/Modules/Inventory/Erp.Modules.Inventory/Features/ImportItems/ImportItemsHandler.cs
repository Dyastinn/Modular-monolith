using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

// One row of an item file. Quantity and UnitCost: the opening stock, counted on the day the system goes live.
internal sealed record ItemImportRow(string Sku, string Name, string Unit, bool AllowsFractions = false, decimal Quantity = 0,
    decimal UnitCost = 0);

internal sealed record ImportItemsCommand(IReadOnlyList<ItemImportRow> Rows, bool Commit);

internal sealed class ImportItemsHandler(InventoryDbContext db, ICurrentUser user, TimeProvider time)
{
    private const string OpeningStock = "Opening stock";

    public async Task<ImportResult> HandleAsync(ImportItemsCommand cmd, CancellationToken ct)
    {
        var taken = (await db.Items.Select(i => i.Sku).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var by = new UserId(user.UserId);
        var now = time.GetUtcNow().UtcDateTime;
        var errors = new List<ImportError>();

        // The check runs on stand-in ids, so a preview never uses up item numbers.
        for (var i = 0; i < cmd.Rows.Count; i++)
        {
            try { Build(cmd.Rows[i], new ItemId(0), taken, by, now); }
            catch (DomainException e) { errors.Add(new ImportError(i + 1, e.Message, e.Error.Code)); }
        }
        if (errors.Count > 0 || !cmd.Commit) return new ImportResult(cmd.Rows.Count, 0, errors);

        taken = (await db.Items.Select(i => i.Sku).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in cmd.Rows)
        {
            var (item, opening) = Build(row, new ItemId(await db.NextItemIdAsync(ct)), taken, by, now);
            db.Items.Add(item);
            if (opening is not null) db.StockMovements.Add(opening);
        }
        await db.SaveChangesAsync(ct);   // one transaction: every item and its opening stock, or none
        return new ImportResult(cmd.Rows.Count, cmd.Rows.Count, []);
    }

    private static (Item Item, StockMovement? Opening) Build(ItemImportRow r, ItemId id, HashSet<string> taken, UserId by, DateTime now)
    {
        var item = Item.Create(id, r.Sku, r.Name, r.Unit, r.AllowsFractions);
        if (!taken.Add(item.Sku))
            throw new DomainException(InventoryErrors.SkuTaken, $"An item with SKU {item.Sku} already exists, or appears twice in the file.");
        var opening = r.Quantity > 0 ? item.Adjust(r.Quantity, OpeningStock, by, now, new UnitCost(r.UnitCost)) : null;
        return (item, opening);
    }
}
