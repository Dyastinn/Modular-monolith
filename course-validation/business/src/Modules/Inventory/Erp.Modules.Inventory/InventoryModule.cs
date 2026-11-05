using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

// The module's one public type: how the host plugs it in.
public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration config)
    {
        // Built on the request's shared connection, so Inventory can join a transaction Sales started.
        services.AddDbContext<InventoryDbContext>((sp, o) =>
            InventoryDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection));

        services.AddScoped<IModuleDatabase, InventoryDatabase>();

        // The public contract Sales calls, bound to the internal implementation.
        services.AddScoped<IStockReservation, StockReservationService>();
        return services;
    }
}
