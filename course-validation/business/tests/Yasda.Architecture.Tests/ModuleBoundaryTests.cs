using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Yasda.Modules.Identity;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla;
using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Tickets;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Architecture.Tests;

// CLAUDE.md §4 module rules as tests: a reviewer can miss a stray reference or `public` class; these can't.
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules = [typeof(IdentityModule).Assembly, typeof(TicketsModule).Assembly, typeof(SlaModule).Assembly];

    // Each module's .Contracts project joins this list when it is created.
    private static readonly Assembly[] ContractsAssemblies = [typeof(IUserDirectory).Assembly, typeof(TicketPriority).Assembly, typeof(DeadlinesChanged).Assembly];

    private const BindingFlags Everything =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void A_module_references_other_modules_only_through_their_Contracts()
    {
        foreach (var module in Modules)
        {
            var illegal = new List<string>();
            foreach (var reference in module.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                var isModule = name.StartsWith("Yasda.Modules.", StringComparison.Ordinal);
                var isContracts = name.EndsWith(".Contracts", StringComparison.Ordinal);
                if (isModule && !isContracts)
                    illegal.Add(name);
            }

            illegal.Should().BeEmpty($"{module.GetName().Name} may reference another module's .Contracts only");
        }
    }

    [Fact]
    public void Contracts_reference_no_module_so_no_cycle_can_form()
    {
        foreach (var contracts in ContractsAssemblies)
        {
            var names = contracts.GetReferencedAssemblies().Select(a => a.Name!);
            var modules = names.Where(n => n.StartsWith("Yasda.Modules.", StringComparison.Ordinal));

            modules.Should().BeEmpty($"{contracts.GetName().Name} must not depend on any module");
        }
    }

    [Fact]
    public void Only_the_host_references_shared_infrastructure()
    {
        var checkedAssemblies = new List<Assembly>();
        checkedAssemblies.AddRange(Modules);
        checkedAssemblies.AddRange(ContractsAssemblies);

        foreach (var assembly in checkedAssemblies)
        {
            var names = assembly.GetReferencedAssemblies().Select(a => a.Name);

            names.Should().NotContain("Yasda.Shared.Infrastructure",
                $"{assembly.GetName().Name} must code against BuildingBlocks interfaces, never their implementations");
        }
    }

    [Fact]
    public void A_module_exposes_only_its_entry_class()
    {
        foreach (var module in Modules)
        {
            // EF generates migrations as public classes.
            var exported = module.GetExportedTypes().Where(t => !IsInNamespace(t, ".Migrations"));
            var publicTypes = exported.Select(t => t.FullName);

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
            foreach (var type in module.GetTypes())
            {
                if (!IsInNamespace(type, ".Domain")) continue;

                var usedAssemblies = new List<string>();
                foreach (var usedType in TypesUsedBy(type))
                {
                    var assemblyName = usedType.Assembly.GetName().Name!;
                    foreach (var prefix in forbidden)
                    {
                        if (assemblyName.StartsWith(prefix, StringComparison.Ordinal))
                            usedAssemblies.Add(assemblyName);
                    }
                }

                usedAssemblies.Should().BeEmpty($"{type.FullName} is domain code and must stay free of data access and web types");
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

            var unhandled = new List<string>();
            foreach (var type in module.GetTypes())
            {
                if (IsConcreteEvent(type, typeof(IDomainEvent)) && !handled.Contains(type))
                    unhandled.Add(type.FullName!);
            }

            unhandled.Should().BeEmpty($"every domain event in {module.GetName().Name} needs a handler there");
        }
    }

    // An integration event exists to tell other modules something; one the publisher alone handles is a domain event.
    [Fact]
    public void Every_integration_event_is_handled_by_another_module()
    {
        foreach (var contracts in ContractsAssemblies)
        {
            var owner = contracts.GetName().Name![..^".Contracts".Length];

            var handled = new HashSet<Type>();
            foreach (var module in Modules)
            {
                if (module.GetName().Name == owner) continue;
                handled.UnionWith(HandledBy(module, typeof(IIntegrationEventHandler<>)));
            }

            var unhandled = new List<string>();
            foreach (var type in contracts.GetTypes())
            {
                if (IsConcreteEvent(type, typeof(IIntegrationEvent)) && !handled.Contains(type))
                    unhandled.Add(type.FullName!);
            }

            unhandled.Should().BeEmpty($"every integration event in {contracts.GetName().Name} needs a handler in another module");
        }
    }

    // A public domain event would let another module couple to this module's internals: publish an integration event.
    [Fact]
    public void Domain_events_never_leave_their_module()
    {
        var checkedAssemblies = new List<Assembly>();
        checkedAssemblies.AddRange(Modules);
        checkedAssemblies.AddRange(ContractsAssemblies);

        var publicDomainEvents = new List<string>();
        foreach (var assembly in checkedAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (IsConcreteEvent(type, typeof(IDomainEvent)) && type.IsVisible)
                    publicDomainEvents.Add(type.FullName!);
            }
        }

        publicDomainEvents.Should().BeEmpty("domain events are internal; other modules hear about them through an IIntegrationEvent");
    }

    // Vertical slices: a feature's files sit together. Folders named after a technical layer pull them apart again.
    [Fact]
    public void Code_is_grouped_by_feature_not_by_technical_layer()
    {
        string[] layers = [".Commands", ".Queries", ".Handlers", ".Validators", ".Endpoints", ".Repositories", ".Services"];

        var layerNamespaces = new HashSet<string>();
        foreach (var module in Modules)
        {
            foreach (var type in module.GetTypes())
            {
                foreach (var layer in layers)
                {
                    if (IsInNamespace(type, layer))
                        layerNamespaces.Add(type.Namespace!);
                }
            }
        }

        layerNamespaces.Should().BeEmpty("a feature folder holds its handler and endpoint together");
    }

    // A rich domain changes state through methods that check the rules. A public setter would let anyone skip them.
    [Fact]
    public void Domain_state_changes_only_through_its_methods()
    {
        var setters = new List<string>();
        foreach (var module in Modules)
        {
            foreach (var type in module.GetTypes())
            {
                if (!IsInNamespace(type, ".Domain")) continue;

                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var setter = property.SetMethod;
                    if (setter is null || !setter.IsPublic) continue;

                    // A record's init-only property also has a public setter, marked IsExternalInit; those are fine.
                    var isInitOnly = setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                    if (!isInitOnly)
                        setters.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        setters.Should().BeEmpty("domain properties have private setters; records' init-only properties are fine");
    }

    // CQRS: a command changes state through the domain and EF Core; a query reads with Dapper and SQL functions. A query
    // handler holding a DbContext could write; a command handler using Dapper would skip the domain's rules.
    [Fact]
    public void Queries_read_with_Dapper_and_commands_write_with_EF_Core()
    {
        foreach (var module in Modules)
        {
            foreach (var handler in module.GetTypes())
            {
                if (!IsFeatureHandler(handler)) continue;

                var messageNames = new List<string>();
                foreach (var method in handler.GetMethods())
                {
                    if (method.Name == "HandleAsync")
                        messageNames.Add(method.GetParameters()[0].ParameterType.Name);
                }

                var dependencies = new List<Type>();
                foreach (var parameter in handler.GetConstructors().Single().GetParameters())
                    dependencies.Add(parameter.ParameterType);

                if (IsQueryHandler(handler, messageNames))
                {
                    var dbContexts = new List<string>();
                    foreach (var dependency in dependencies)
                    {
                        if (dependency.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.DbContext)))
                            dbContexts.Add(dependency.Name);
                    }
                    dbContexts.Should().BeEmpty($"{handler.Name} is a query: it reads with Dapper, never through a DbContext");
                }
                else if (EndsWithAny(messageNames, "Command"))
                {
                    dependencies.Should().NotContain(typeof(IDbConnectionFactory),
                        $"{handler.Name} is a command: it writes through the DbContext and the domain, never with Dapper");
                }
            }
        }
    }

    private static bool IsInNamespace(Type type, string suffix) =>
        (type.Namespace ?? "").EndsWith(suffix, StringComparison.Ordinal);

    private static bool IsFeatureHandler(Type type) =>
        type.Name.EndsWith("Handler", StringComparison.Ordinal) && IsInNamespace(type, ".Features");

    private static bool IsConcreteEvent(Type type, Type eventInterface) =>
        !type.IsAbstract && !type.IsInterface && eventInterface.IsAssignableFrom(type);

    private static bool IsQueryHandler(Type handler, List<string> messageNames)
    {
        if (EndsWithAny(messageNames, "Query")) return true;
        if (handler.Name.StartsWith("Get", StringComparison.Ordinal)) return true;
        if (handler.Name.StartsWith("List", StringComparison.Ordinal)) return true;
        if (handler.Name.StartsWith("Search", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool EndsWithAny(List<string> names, string suffix)
    {
        foreach (var name in names)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Every type a class's fields, constructors and methods mention.
    private static List<Type> TypesUsedBy(Type type)
    {
        var used = new List<Type>();
        foreach (var field in type.GetFields(Everything))
            used.Add(field.FieldType);

        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
                used.Add(parameter.ParameterType);
        }

        foreach (var method in type.GetMethods(Everything | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
                used.Add(parameter.ParameterType);
            used.Add(method.ReturnType);
        }
        return used;
    }

    // The event types that some class in the module handles through the given open handler interface.
    private static HashSet<Type> HandledBy(Assembly module, Type openHandler)
    {
        var handled = new HashSet<Type>();
        foreach (var type in module.GetTypes())
        {
            foreach (var implemented in type.GetInterfaces())
            {
                if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openHandler)
                    handled.Add(implemented.GetGenericArguments()[0]);
            }
        }
        return handled;
    }
}
