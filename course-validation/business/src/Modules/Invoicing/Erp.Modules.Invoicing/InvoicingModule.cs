using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Features;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        services.AddScoped<InvoiceIssuer>();
        services.AddScoped<IInvoicingSetup, InvoicingSetupService>();

        // PDFsharp's font resolver is process-wide and may be set only once.
        lock (FontLock)
            GlobalFontSettings.FontResolver ??= new ErpFontResolver(config["Invoicing:FontPath"], config["Invoicing:BoldFontPath"]);

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<GetCompanyProfileHandler>();
        services.AddScoped<UpdateCompanyProfileHandler>();
        services.AddScoped<SearchInvoicesHandler>();
        services.AddScoped<GetInvoiceHandler>();
        services.AddScoped<PrintInvoiceHandler>();
        services.AddScoped<VoidInvoiceHandler>();
        services.AddScoped<ReissueInvoiceHandler>();
        services.AddScoped<RecordPaymentHandler>();
        services.AddScoped<VoidPaymentHandler>();
        services.AddScoped<ReceivablesAgingHandler>();
        services.AddScoped<WithholdingSummaryHandler>();
        services.AddScoped<RecordWithholdingCertificateHandler>();
        services.AddScoped<VoidWithholdingCertificateHandler>();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        // Events: each module registers the handlers it owns. An unregistered handler never runs, and a test checks.
        services.AddScoped<IDomainEventHandler<InvoicePaymentRecorded>, PublishPaymentRecorded>();
        services.AddScoped<IDomainEventHandler<InvoicePaymentVoided>, PublishPaymentVoided>();
        services.AddScoped<IIntegrationEventHandler<ShipmentRecorded>, ShipmentRecordedHandler>();
        services.AddSingleton(ErrorCatalog.From(typeof(InvoicingErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapInvoicingEndpoints(this IEndpointRouteBuilder api)
    {
        var invoicing = api.MapGroup("").WithTags("Invoicing");
        CompanyProfileEndpoint.Map(invoicing);
        SearchInvoicesEndpoint.Map(invoicing);
        GetInvoiceEndpoint.Map(invoicing);
        VoidInvoiceEndpoint.Map(invoicing);
        RecordPaymentEndpoint.Map(invoicing);
        ReceivablesAgingEndpoint.Map(invoicing);
        WithholdingTaxEndpoint.Map(invoicing);
        return api;
    }
}
