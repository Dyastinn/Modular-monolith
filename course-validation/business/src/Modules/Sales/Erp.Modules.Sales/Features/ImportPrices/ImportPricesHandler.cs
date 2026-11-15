using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

// One row of a price list file: the item by SKU and its category by name, the way a spreadsheet has them.
internal sealed record PriceRow(string Sku, decimal UnitPrice, string Category, bool IsForSale = true, TaxType TaxType = TaxType.Vatable,
    bool SpecialDiscountEligible = false);

internal sealed record ImportPricesCommand(IReadOnlyList<PriceRow> Rows, bool Commit);

internal sealed class ImportPricesHandler(SalesDbContext db)
{
    public async Task<ImportResult> HandleAsync(ImportPricesCommand cmd, CancellationToken ct)
    {
        var skus = cmd.Rows.Select(r => r.Sku.Trim().ToUpperInvariant()).Distinct().ToList();
        var items = await db.SalesItems.Where(i => skus.Contains(i.Sku)).ToDictionaryAsync(i => i.Sku, StringComparer.OrdinalIgnoreCase, ct);
        var categories = await db.ItemCategories.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportError>();

        for (var i = 0; i < cmd.Rows.Count; i++)
        {
            var r = cmd.Rows[i];
            try
            {
                var sku = r.Sku.Trim();
                // A new catalog item reaches Sales a moment after Inventory creates it: import items first, then prices.
                var item = items.GetValueOrDefault(sku)
                    ?? throw new DomainException(SalesErrors.ItemNotFound, $"There is no item with SKU '{sku}'. Import the items first.");
                if (!seen.Add(sku)) throw new DomainException(SalesErrors.DuplicateRow, $"SKU '{sku}' appears more than once in the file.");
                var category = categories.GetValueOrDefault(r.Category.Trim())
                    ?? throw new DomainException(SalesErrors.CategoryNotFound, $"There is no category named '{r.Category.Trim()}'.");
                item.SetPricing(new Money(r.UnitPrice), category, r.IsForSale, r.TaxType, r.SpecialDiscountEligible);
            }
            catch (DomainException e)
            {
                errors.Add(new ImportError(i + 1, e.Message, e.Error.Code));
            }
        }

        if (errors.Count > 0 || !cmd.Commit) return new ImportResult(cmd.Rows.Count, 0, errors);
        await db.SaveChangesAsync(ct);
        return new ImportResult(cmd.Rows.Count, cmd.Rows.Count, []);
    }
}
