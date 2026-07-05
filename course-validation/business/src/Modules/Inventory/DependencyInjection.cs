using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration config)
    {
        // The contract Sales calls to reserve/release stock (Phase 7). Neither module references the other.
        services.AddScoped<IStockReservation, StockReservationService>();
        return services;
    }
}
