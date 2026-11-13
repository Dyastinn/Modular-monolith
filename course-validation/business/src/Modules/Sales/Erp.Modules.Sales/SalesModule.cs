using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Features;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales;

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        var assembly = typeof(SalesModule).Assembly;

        services.AddDbContext<SalesDbContext>((sp, o) =>
            SalesDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, SalesDatabase>();
        services.AddScoped<ISalesOrders, SalesOrdersService>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<CreateCustomerHandler>();
        services.AddScoped<SearchCustomersHandler>();
        services.AddScoped<UpdateCustomerHandler>();
        services.AddScoped<SetCustomerBlockedHandler>();
        services.AddScoped<CreateSalesOrderHandler>();
        services.AddScoped<GetSalesOrderHandler>();
        services.AddScoped<SearchSalesOrdersHandler>();
        services.AddScoped<UpdateSalesOrderHandler>();
        services.AddScoped<SubmitSalesOrderHandler>();
        services.AddScoped<ApproveSalesOrderHandler>();
        services.AddScoped<RejectSalesOrderHandler>();
        services.AddScoped<CancelSalesOrderHandler>();
        services.AddScoped<ListItemCategoriesHandler>();
        services.AddScoped<SaveItemCategoryHandler>();
        services.AddScoped<SearchPriceListHandler>();
        services.AddScoped<SetItemPricingHandler>();
        services.AddScoped<ListSpecialDiscountProgramsHandler>();
        services.AddScoped<SaveSpecialDiscountProgramHandler>();
        services.AddScoped<ApplySpecialDiscountHandler>();
        services.AddScoped<RemoveSpecialDiscountHandler>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        // Events: each module registers the handlers it owns. An unregistered handler never runs, and a test checks.
        services.AddScoped<IIntegrationEventHandler<ItemCatalogChanged>, ItemCatalogChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<PaymentRecorded>, PaymentRecordedHandler>();
        services.AddScoped<IIntegrationEventHandler<PaymentVoided>, PaymentVoidedHandler>();
        services.AddScoped<IDomainEventHandler<SalesOrderApproved>, SalesOrderApprovedAuditHandler>();
        services.AddSingleton(ErrorCatalog.From(typeof(SalesErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder api)
    {
        var sales = api.MapGroup("").WithTags("Sales");
        CreateCustomerEndpoint.Map(sales);
        SearchCustomersEndpoint.Map(sales);
        UpdateCustomerEndpoint.Map(sales);
        CreateSalesOrderEndpoint.Map(sales);
        GetSalesOrderEndpoint.Map(sales);
        SearchSalesOrdersEndpoint.Map(sales);
        UpdateSalesOrderEndpoint.Map(sales);
        SubmitSalesOrderEndpoint.Map(sales);
        ApproveSalesOrderEndpoint.Map(sales);
        RejectSalesOrderEndpoint.Map(sales);
        CancelSalesOrderEndpoint.Map(sales);
        ItemCategoriesEndpoint.Map(sales);
        SearchPriceListEndpoint.Map(sales);
        SetItemPricingEndpoint.Map(sales);
        SpecialDiscountEndpoint.Map(sales);
        return api;
    }
}
