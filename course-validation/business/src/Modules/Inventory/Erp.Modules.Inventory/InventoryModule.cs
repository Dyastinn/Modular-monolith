using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Features;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<CreateItemHandler>();
        services.AddScoped<UpdateItemHandler>();
        services.AddScoped<SetItemActiveHandler>();
        services.AddScoped<SearchItemsHandler>();
        services.AddScoped<AdjustStockHandler>();
        services.AddScoped<GetItemMovementsHandler>();
        services.AddScoped<ShipSalesOrderHandler>();
        services.AddScoped<SearchShipmentsHandler>();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        // Events: each module registers the handlers it owns. An unregistered handler never runs, and a test checks.
        services.AddScoped<IDomainEventHandler<ItemChanged>, PublishItemCatalogChanged>();
        services.AddScoped<IDomainEventHandler<ShipmentCreated>, PublishShipmentRecorded>();
        services.AddSingleton(ErrorCatalog.From(typeof(InventoryErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("").WithTags("Inventory");
        CreateItemEndpoint.Map(inventory);
        UpdateItemEndpoint.Map(inventory);
        SetItemActiveEndpoint.Map(inventory);
        SearchItemsEndpoint.Map(inventory);
        AdjustStockEndpoint.Map(inventory);
        GetItemMovementsEndpoint.Map(inventory);
        ShipSalesOrderEndpoint.Map(inventory);
        SearchShipmentsEndpoint.Map(inventory);
        return api;
    }
}
