using Carter;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(DependencyInjection).Assembly;   // the Erp.Modules.Sales assembly

        // Register every IRequestHandler<,> defined in THIS module.
        var handlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => new { Implementation = t, Service = i }));
        foreach (var h in handlerTypes) services.AddTransient(h.Service, h.Implementation);

        // Register every IDomainEventHandler<> defined in THIS module (same reflection loop).
        var eventHandlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(i => new { Implementation = t, Service = i }));
        foreach (var h in eventHandlerTypes) services.AddTransient(h.Service, h.Implementation);

        // Register every ICarterModule endpoint defined in THIS module. Carter's own AddCarter() scan only
        // picks up PUBLIC types, and since Phase 15b every endpoint here is `internal` — so AddCarter() sees
        // none of them and the routes silently vanish. We register them ourselves, from inside the assembly
        // where `internal` IS visible. See plan-business-tutorial/15b-internal-all-the-way.md §4.
        var endpointTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ICarterModule).IsAssignableFrom(t));
        foreach (var e in endpointTypes) services.AddSingleton(typeof(ICarterModule), e);

        // includeInternalTypes: true is REQUIRED now the validators are internal. Without it FluentValidation
        // finds nothing, registers nothing, and throws no error — validation just silently stops running.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // This module's repositories (the ports declared in Domain).
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();

        return services;
    }
}
