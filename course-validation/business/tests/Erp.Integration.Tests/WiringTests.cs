using AwesomeAssertions;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Invoicing;
using Erp.Modules.Purchasing;
using Erp.Modules.Sales;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

// Each module registers its own handlers by hand. A handler that's never registered compiles and passes every unit test;
// then an event handler silently never runs, or an endpoint fails at startup. These ask the running app's container.
[Collection("Integration")]
public class WiringTests(CustomWebAppFactory factory)
{
    private static readonly Type[] Modules =
        [typeof(IdentityModule), typeof(SalesModule), typeof(InventoryModule), typeof(PurchasingModule), typeof(InvoicingModule)];

    [Fact]
    public async Task Every_feature_handler_is_registered()
    {
        var handlers = Modules.SelectMany(m => m.Assembly.GetTypes())
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal) && (t.Namespace ?? "").EndsWith(".Features", StringComparison.Ordinal))
            .ToList();

        await using var scope = factory.Services.CreateAsyncScope();
        var isService = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        handlers.Should().NotBeEmpty();
        handlers.Where(h => !isService.IsService(h)).Select(h => h.Name).Should().BeEmpty("an endpoint can only use a registered handler");
    }

    [Fact]
    public async Task Every_event_handler_class_is_registered()
    {
        Type[] handlerInterfaces = [typeof(IDomainEventHandler<>), typeof(IIntegrationEventHandler<>)];
        var handlers = Modules.SelectMany(m => m.Assembly.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()))
                .Select(i => (Class: t, Interface: i)))
            .ToList();

        await using var scope = factory.Services.CreateAsyncScope();
        handlers.Should().NotBeEmpty();
        handlers.Where(h => !scope.ServiceProvider.GetServices(h.Interface).Any(s => s!.GetType() == h.Class))
            .Select(h => $"{h.Class.Name} as {h.Interface.Name}<{h.Interface.GetGenericArguments()[0].Name}>")
            .Should().BeEmpty("a handler that isn't registered never runs");
    }
}
