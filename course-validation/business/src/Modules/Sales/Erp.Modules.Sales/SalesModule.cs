using Carter;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales;

// The module's one public type: how the host plugs it in. Handlers need no registration here — the Mediator
// source generator (running in Erp.Api) finds them at compile time.
public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(SalesModule).Assembly;

        services.AddDbContext<SalesDbContext>((sp, o) =>
            SalesDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection));
        services.AddScoped<IModuleDatabase, SalesDatabase>();

        // Carter's AddCarter() only discovers PUBLIC endpoint classes; ours are internal, so register them here
        // or every /sales-orders route silently 404s.
        foreach (var endpoint in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)))
            services.AddSingleton(typeof(ICarterModule), endpoint);

        // includeInternalTypes: true is REQUIRED — without it no validator is found and none ever runs, silently.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
