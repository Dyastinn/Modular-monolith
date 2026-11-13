using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Features;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Purchasing;

public static class PurchasingModule
{
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services)
    {
        var assembly = typeof(PurchasingModule).Assembly;

        services.AddDbContext<PurchasingDbContext>((sp, o) =>
            PurchasingDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, PurchasingDatabase>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<SaveSupplierHandler>();
        services.AddScoped<SetSupplierActiveHandler>();
        services.AddScoped<SearchSuppliersHandler>();
        services.AddScoped<SavePurchaseOrderHandler>();
        services.AddScoped<SearchPurchaseOrdersHandler>();
        services.AddScoped<GetPurchaseOrderHandler>();
        services.AddScoped<ChangePurchaseOrderStatusHandler>();
        services.AddScoped<ReceivePurchaseOrderHandler>();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(PurchasingErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder api)
    {
        var purchasing = api.MapGroup("").WithTags("Purchasing");
        SaveSupplierEndpoint.Map(purchasing);
        SearchSuppliersEndpoint.Map(purchasing);
        SavePurchaseOrderEndpoint.Map(purchasing);
        SearchPurchaseOrdersEndpoint.Map(purchasing);
        ChangePurchaseOrderStatusEndpoint.Map(purchasing);
        ReceivePurchaseOrderEndpoint.Map(purchasing);
        return api;
    }
}
