using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Yasda.Modules.Identity;
using Yasda.Modules.Tickets;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Architecture.Tests;

// The error catalog is a promise to the people who look codes up: unique, documented, and never a dead entry.
public class ErrorCatalogTests
{
    // Every new module's catalog joins this list, with its prefix.
    private static readonly Dictionary<Type, string> Catalogs = new()
    {
        [typeof(CommonErrors)] = "YASDA",
        [CatalogIn(typeof(IdentityModule))] = "IDENTITY",
        [CatalogIn(typeof(TicketsModule))] = "TICKETS",
    };

    // The module's one XxxErrors class in its Domain folder.
    private static Type CatalogIn(Type moduleEntry)
    {
        var catalogs = new List<Type>();
        foreach (var type in moduleEntry.Assembly.GetTypes())
        {
            var isErrors = type.Name.EndsWith("Errors", StringComparison.Ordinal);
            var isDomain = (type.Namespace ?? "").EndsWith(".Domain", StringComparison.Ordinal);
            if (isErrors && isDomain)
                catalogs.Add(type);
        }
        return catalogs.Single();
    }

    private static List<(Type Catalog, FieldInfo Field, ErrorCode Error)> AllCodes()
    {
        var all = new List<(Type, FieldInfo, ErrorCode)>();
        foreach (var catalog in Catalogs.Keys)
        {
            foreach (var field in catalog.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(ErrorCode))
                    all.Add((catalog, field, (ErrorCode)field.GetValue(null)!));
            }
        }
        return all;
    }

    [Fact]
    public void Every_code_is_unique_well_formed_and_documented()
    {
        var codes = AllCodes();

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
        var catalogFileNames = new HashSet<string>();
        foreach (var catalog in Catalogs.Keys)
            catalogFileNames.Add(catalog.Name);

        // All source code except build output and the catalog files themselves (a code is "used" outside them).
        var objFolder = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var source = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(objFolder, StringComparison.Ordinal)) continue;
            if (catalogFileNames.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            source.AppendLine(File.ReadAllText(file));
        }
        var allSource = source.ToString();

        var unused = new List<string>();
        foreach (var (catalog, field, error) in AllCodes())
        {
            if (!Regex.IsMatch(allSource, $@"\b{catalog.Name}\.{field.Name}\b"))
                unused.Add($"{error.Code} ({catalog.Name}.{field.Name})");
        }

        unused.Should().BeEmpty("every code in the catalog must be thrown or returned somewhere");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Yasda.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Yasda.slnx not found above the test output folder.");
    }
}
