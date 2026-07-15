using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration config)
    {
        // The public contract Sales calls, bound to the internal implementation. Registering an internal
        // type against a public interface is fine — DI resolves it from inside this assembly.
        services.AddScoped<IStockReservation, StockReservationService>();
        return services;
    }
}
