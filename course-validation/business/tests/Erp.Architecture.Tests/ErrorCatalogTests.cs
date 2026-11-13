using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Invoicing;
using Erp.Modules.Purchasing;
using Erp.Modules.Sales;
using Erp.Shared.BuildingBlocks;

namespace Erp.Architecture.Tests;

// The error catalog is a promise to the people who look codes up: unique, documented, and never a dead entry.
public class ErrorCatalogTests
{
    // Every new module's catalog joins this list, with its prefix.
    private static readonly Dictionary<Type, string> Catalogs = new[]
    {
        (typeof(CommonErrors), "ERP"),
        (CatalogIn(typeof(IdentityModule)), "IDENTITY"), (CatalogIn(typeof(SalesModule)), "SALES"),
        (CatalogIn(typeof(InventoryModule)), "INVENTORY"), (CatalogIn(typeof(PurchasingModule)), "PURCHASING"),
        (CatalogIn(typeof(InvoicingModule)), "INVOICING")
    }.ToDictionary(c => c.Item1, c => c.Item2);

    private static Type CatalogIn(Type moduleEntry) =>
        moduleEntry.Assembly.GetTypes().Single(t => t.Name.EndsWith("Errors", StringComparison.Ordinal)
                                                    && t.Namespace!.EndsWith(".Domain", StringComparison.Ordinal));

    private static IEnumerable<(Type Catalog, FieldInfo Field, ErrorCode Error)> AllCodes() =>
        Catalogs.Keys.SelectMany(c => c.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ErrorCode))
            .Select(f => (c, f, (ErrorCode)f.GetValue(null)!)));

    [Fact]
    public void Every_code_is_unique_well_formed_and_documented()
    {
        var codes = AllCodes().ToList();

        codes.Select(c => c.Error.Code).Should().OnlyHaveUniqueItems("a code names one problem, forever");
        foreach (var (catalog, field, error) in codes)
        {
            error.Code.Should().MatchRegex($"^{Catalogs[catalog]}-[0-9]{{4}}$", $"{catalog.Name}.{field.Name} uses its module's prefix");
            error.Title.Should().NotBeNullOrWhiteSpace($"{error.Code} needs a title");
            error.Fix.Should().NotBeNullOrWhiteSpace($"{error.Code} needs a fix: the catalog exists to say what to do");
        }
    }

    // A code nobody throws is a promise nobody keeps: someone would look it up for an error that can't happen.
    [Fact]
    public void Every_code_is_used_somewhere()
    {
        var source = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !Catalogs.Keys.Any(c => Path.GetFileNameWithoutExtension(f) == c.Name))
            .Select(File.ReadAllText)
            .Aggregate(new System.Text.StringBuilder(), (all, text) => all.AppendLine(text))
            .ToString();

        AllCodes().Where(c => !Regex.IsMatch(source, $@"\b{c.Catalog.Name}\.{c.Field.Name}\b"))
            .Select(c => $"{c.Error.Code} ({c.Catalog.Name}.{c.Field.Name})")
            .Should().BeEmpty("every code in the catalog must be thrown or returned somewhere");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Erp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Erp.slnx not found above the test output folder.");
    }
}
