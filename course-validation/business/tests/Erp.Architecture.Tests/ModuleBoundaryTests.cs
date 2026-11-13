using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Invoicing;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Purchasing;
using Erp.Modules.Sales;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Architecture.Tests;

// CLAUDE.md §4 module rules as tests: a reviewer can miss a stray reference or `public` class; these can't.
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(IdentityModule).Assembly, typeof(SalesModule).Assembly, typeof(InventoryModule).Assembly,
        typeof(PurchasingModule).Assembly, typeof(InvoicingModule).Assembly
    ];

    private static readonly Assembly[] ContractsAssemblies =
        [typeof(IStockReservation).Assembly, typeof(ISalesOrders).Assembly, typeof(PaymentRecorded).Assembly];

    [Fact]
    public void A_module_references_other_modules_only_through_their_Contracts()
    {
        foreach (var module in Modules)
        {
            var illegal = module.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules.", StringComparison.Ordinal) && !n.EndsWith(".Contracts", StringComparison.Ordinal));

            illegal.Should().BeEmpty($"{module.GetName().Name} may reference another module's .Contracts only");
        }
    }

    [Fact]
    public void Contracts_reference_no_module_so_no_cycle_can_form()
    {
        foreach (var contracts in ContractsAssemblies)
            contracts.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules.", StringComparison.Ordinal))
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
                .Where(t => !(t.Namespace ?? "").EndsWith(".Migrations", StringComparison.Ordinal))
                .Select(t => t.FullName);

            publicTypes.Should().ContainSingle($"{module.GetName().Name} should expose only its AddXModule entry class")
                .Which.Should().EndWith("Module");
        }
    }

    // Not even the host: it reaches a module only through AddXModule and MapXEndpoints, so the compiler itself keeps
    // Program.cs away from a module's DbContext and handlers.
    [Fact]
    public void Internals_are_shared_only_with_tests()
    {
        foreach (var module in Modules)
        {
            var friends = module.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName);

            friends.Should().OnlyContain(f => f.EndsWith("Tests", StringComparison.Ordinal),
                $"{module.GetName().Name} may grant InternalsVisibleTo to test projects only, never to the host or another module");
        }
    }

    [Fact]
    public void The_domain_does_not_depend_on_data_access_or_web()
    {
        string[] forbidden = ["Microsoft.EntityFrameworkCore", "Npgsql", "Dapper", "Microsoft.AspNetCore"];

        foreach (var module in Modules)
        {
            var domainTypes = module.GetTypes().Where(t => (t.Namespace ?? "").EndsWith(".Domain", StringComparison.Ordinal));
            foreach (var type in domainTypes)
            {
                var used = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(f => f.FieldType)
                    .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                    .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));

                used.Select(t => t.Assembly.GetName().Name!)
                    .Where(a => forbidden.Any(f => a.StartsWith(f, StringComparison.Ordinal)))
                    .Should().BeEmpty($"{type.FullName} is domain code and must stay free of data access and web types");
            }
        }
    }

    // Raise an event only with its first handler. An event nobody handles is a feature that silently does nothing.
    [Fact]
    public void Every_domain_event_has_a_handler_in_its_own_module()
    {
        foreach (var module in Modules)
        {
            var handled = HandledBy(module, typeof(IDomainEventHandler<>));
            module.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t))
                .Where(e => !handled.Contains(e)).Select(e => e.FullName)
                .Should().BeEmpty($"every domain event in {module.GetName().Name} needs a handler there");
        }
    }

    // An integration event exists to tell other modules something; one the publisher alone handles is a domain event.
    [Fact]
    public void Every_integration_event_is_handled_by_another_module()
    {
        foreach (var contracts in ContractsAssemblies)
        {
            var owner = contracts.GetName().Name![..^".Contracts".Length];
            var handled = Modules.Where(m => m.GetName().Name != owner)
                .SelectMany(m => HandledBy(m, typeof(IIntegrationEventHandler<>))).ToHashSet();

            contracts.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
                .Where(e => !handled.Contains(e)).Select(e => e.FullName)
                .Should().BeEmpty($"every integration event in {contracts.GetName().Name} needs a handler in another module");
        }
    }

    // A public domain event would let another module couple to this module's internals: publish an integration event.
    [Fact]
    public void Domain_events_never_leave_their_module()
    {
        Modules.Concat(ContractsAssemblies).SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsVisible: true } && typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .Should().BeEmpty("domain events are internal; other modules hear about them through an IIntegrationEvent");
    }

    private static HashSet<Type> HandledBy(Assembly module, Type openHandler) =>
        module.GetTypes()
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHandler)
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

    // Vertical slices: a feature's files sit together. Folders named after a technical layer pull them apart again.
    [Fact]
    public void Code_is_grouped_by_feature_not_by_technical_layer()
    {
        string[] layers = [".Commands", ".Queries", ".Handlers", ".Validators", ".Endpoints", ".Repositories", ".Services"];

        Modules.SelectMany(m => m.GetTypes())
            .Select(t => t.Namespace ?? "")
            .Distinct()
            .Where(ns => layers.Any(l => ns.EndsWith(l, StringComparison.Ordinal)))
            .Should().BeEmpty("a feature folder holds its handler and endpoint together");
    }

    // A rich domain changes state through methods that check the rules. A public setter would let anyone skip them.
    [Fact]
    public void Domain_state_changes_only_through_its_methods()
    {
        var setters = Modules.SelectMany(m => m.GetTypes())
            .Where(t => (t.Namespace ?? "").EndsWith(".Domain", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod is { IsPublic: true } setter
                        && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        setters.Should().BeEmpty("domain properties have private setters; records' init-only properties are fine");
    }

    // CQRS: a command changes state through the domain and EF Core; a query reads with Dapper and SQL functions. A query
    // handler holding a DbContext could write; a command handler using Dapper would skip the domain's rules.
    [Fact]
    public void Queries_read_with_Dapper_and_commands_write_with_EF_Core()
    {
        foreach (var handler in Modules.SelectMany(m => m.GetTypes()).Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal) && (t.Namespace ?? "").EndsWith(".Features", StringComparison.Ordinal)))
        {
            var messages = handler.GetMethods().Where(m => m.Name == "HandleAsync")
                .Select(m => m.GetParameters().First().ParameterType.Name).ToList();
            var dependencies = handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
            var isQuery = messages.Any(m => m.EndsWith("Query", StringComparison.Ordinal))
                          || handler.Name.StartsWith("Get", StringComparison.Ordinal) || handler.Name.StartsWith("List", StringComparison.Ordinal) || handler.Name.StartsWith("Search", StringComparison.Ordinal);

            if (isQuery)
                dependencies.Where(d => d.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.DbContext))).Select(d => d.Name)
                    .Should().BeEmpty($"{handler.Name} is a query: it reads with Dapper, never through a DbContext");
            else if (messages.Any(m => m.EndsWith("Command", StringComparison.Ordinal)))
                dependencies.Should().NotContain(typeof(IDbConnectionFactory),
                    $"{handler.Name} is a command: it writes through the DbContext and the domain, never with Dapper");
        }
    }
}
