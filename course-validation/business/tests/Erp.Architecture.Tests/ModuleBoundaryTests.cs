using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales;

namespace Erp.Architecture.Tests;

// CLAUDE.md §4 module rules as tests: a reviewer can miss a stray reference or `public` class; these can't.
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules =
        [typeof(IdentityModule).Assembly, typeof(SalesModule).Assembly, typeof(InventoryModule).Assembly];

    private static readonly Assembly[] ContractsAssemblies = [typeof(IStockReservation).Assembly];

    [Fact]
    public void A_module_references_other_modules_only_through_their_Contracts()
    {
        foreach (var module in Modules)
        {
            var illegal = module.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules.") && !n.EndsWith(".Contracts"));

            illegal.Should().BeEmpty($"{module.GetName().Name} may reference another module's .Contracts only");
        }
    }

    [Fact]
    public void Contracts_reference_no_module_so_no_cycle_can_form()
    {
        foreach (var contracts in ContractsAssemblies)
            contracts.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules."))
                .Should().BeEmpty($"{contracts.GetName().Name} must not depend on any module");
    }

    [Fact]
    public void Only_the_host_references_shared_infrastructure()
    {
        foreach (var module in Modules.Concat(ContractsAssemblies))
            module.GetReferencedAssemblies().Select(a => a.Name)
                .Should().NotContain("Erp.Shared.Infrastructure",
                    $"{module.GetName().Name} must code against BuildingBlocks interfaces, never their implementations");
    }

    [Fact]
    public void A_module_exposes_only_its_entry_class()
    {
        foreach (var module in Modules)
        {
            var publicTypes = module.GetExportedTypes()
                // EF generates migrations as public classes.
                .Where(t => !(t.Namespace ?? "").EndsWith(".Migrations"))
                .Select(t => t.FullName);

            publicTypes.Should().ContainSingle($"{module.GetName().Name} should expose only its AddXModule entry class")
                .Which.Should().EndWith("Module");
        }
    }

    [Fact]
    public void Internals_are_shared_only_with_the_host_and_tests()
    {
        foreach (var module in Modules)
        {
            var friends = module.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName);

            friends.Should().OnlyContain(f => f == "Erp.Api" || f.EndsWith("Tests"),
                $"{module.GetName().Name} may grant InternalsVisibleTo to Erp.Api (Mediator generator) and test projects — never another module");
        }
    }

    [Fact]
    public void The_domain_does_not_depend_on_data_access_or_web()
    {
        string[] forbidden = ["Microsoft.EntityFrameworkCore", "Npgsql", "Dapper", "Microsoft.AspNetCore", "Carter"];

        foreach (var module in Modules)
        {
            var domainTypes = module.GetTypes().Where(t => (t.Namespace ?? "").EndsWith(".Domain"));
            foreach (var type in domainTypes)
            {
                var used = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(f => f.FieldType)
                    .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                    .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));

                used.Select(t => t.Assembly.GetName().Name!)
                    .Where(a => forbidden.Any(a.StartsWith))
                    .Should().BeEmpty($"{type.FullName} is domain code and must stay free of data access and web types");
            }
        }
    }
}
