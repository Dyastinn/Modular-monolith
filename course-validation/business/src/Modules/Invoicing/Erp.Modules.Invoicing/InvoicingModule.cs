using Carter;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Fonts;

namespace Erp.Modules.Invoicing;

public static class InvoicingModule
{
    private static readonly Lock FontLock = new();

    public static IServiceCollection AddInvoicingModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(InvoicingModule).Assembly;

        services.AddDbContext<InvoicingDbContext>((sp, o) =>
            InvoicingDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, InvoicingDatabase>();
        services.AddSingleton(InvoicingSettings.From(config));
        services.AddScoped<InvoiceIssuer>();

        // PDFsharp's font resolver is process-wide and may be set only once.
        lock (FontLock)
            GlobalFontSettings.FontResolver ??= new ErpFontResolver(config["Invoicing:FontPath"], config["Invoicing:BoldFontPath"]);

        foreach (var endpoint in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)))
            services.AddSingleton(typeof(ICarterModule), endpoint);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
