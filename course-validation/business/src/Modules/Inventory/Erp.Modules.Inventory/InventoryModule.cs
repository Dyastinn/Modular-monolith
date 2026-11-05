using Microsoft.EntityFrameworkCore.Diagnostics;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        // On the shared connection, so Inventory can join a transaction Sales started.
        services.AddDbContext<InventoryDbContext>((sp, o) =>
            InventoryDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));

        services.AddScoped<IModuleDatabase, InventoryDatabase>();

        services.AddScoped<IStockReservation, StockReservationService>();
        return services;
    }
}
