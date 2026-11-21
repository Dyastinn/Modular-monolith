using AwesomeAssertions;
using Yasda.Modules.Identity;
using Yasda.Modules.Knowledge;
using Yasda.Modules.Sla;
using Yasda.Modules.Tickets;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Integration.Tests;

// Each module registers its own handlers by hand. A handler that's never registered compiles and passes every unit test;
// then an event handler silently never runs, or an endpoint fails at startup. These ask the running app's container.
[Collection("Integration")]
public class WiringTests(CustomWebAppFactory factory)
{
    private static readonly Type[] Modules =
        [typeof(IdentityModule), typeof(TicketsModule), typeof(SlaModule), typeof(KnowledgeModule)];

    [Fact]
    public async Task Every_feature_handler_is_registered()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var isService = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

        var unregistered = new List<string>();
        foreach (var module in Modules)
        {
            foreach (var type in module.Assembly.GetTypes())
            {
                var isHandler = type.Name.EndsWith("Handler", StringComparison.Ordinal);
                var isFeature = (type.Namespace ?? "").EndsWith(".Features", StringComparison.Ordinal);
                if (isHandler && isFeature && !isService.IsService(type))
                    unregistered.Add(type.Name);
            }
        }

        unregistered.Should().BeEmpty("an endpoint can only use a registered handler");
    }

    [Fact]
    public async Task Every_event_handler_class_is_registered()
    {
        Type[] handlerInterfaces = [typeof(IDomainEventHandler<>), typeof(IIntegrationEventHandler<>)];
        await using var scope = factory.Services.CreateAsyncScope();

        var unregistered = new List<string>();
        foreach (var module in Modules)
        {
            foreach (var type in module.Assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;

                foreach (var implemented in type.GetInterfaces())
                {
                    if (!implemented.IsGenericType) continue;
                    if (!handlerInterfaces.Contains(implemented.GetGenericTypeDefinition())) continue;

                    if (!IsRegisteredAs(scope.ServiceProvider, implemented, type))
                        unregistered.Add($"{type.Name} as {implemented.Name}<{implemented.GetGenericArguments()[0].Name}>");
                }
            }
        }

        unregistered.Should().BeEmpty("a handler that isn't registered never runs");
    }

    private static bool IsRegisteredAs(IServiceProvider services, Type handlerInterface, Type handlerClass)
    {
        foreach (var registered in services.GetServices(handlerInterface))
        {
            if (registered!.GetType() == handlerClass) return true;
        }
        return false;
    }
}
