using Carter;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        var assembly = typeof(InventoryModule).Assembly;

        // On the shared connection, so Inventory and another module can share one transaction.
        services.AddDbContext<InventoryDbContext>((sp, o) =>
            InventoryDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, InventoryDatabase>();

        services.AddScoped<StockService>();
        services.AddScoped<IStockReservation>(sp => sp.GetRequiredService<StockService>());
        services.AddScoped<IStockReceiving>(sp => sp.GetRequiredService<StockService>());
        services.AddScoped<IItemCatalog>(sp => sp.GetRequiredService<StockService>());

        // AddCarter() only finds public endpoint classes; ours are internal.
        foreach (var endpoint in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)))
            services.AddSingleton(typeof(ICarterModule), endpoint);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
