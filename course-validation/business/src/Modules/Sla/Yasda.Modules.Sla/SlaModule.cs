using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Features;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Modules.Sla;

public static class SlaModule
{
    public static IServiceCollection AddSlaModule(this IServiceCollection services)
    {
        services.AddDbContext<SlaDbContext>((sp, o) =>
            SlaDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, SlaDatabase>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<GetPoliciesHandler>();
        services.AddScoped<ChangePolicyHandler>();
        services.AddScoped<GetCalendarHandler>();
        services.AddScoped<ChangeCalendarHandler>();
        services.AddScoped<AddHolidayHandler>();
        services.AddScoped<RemoveHolidayHandler>();
        services.AddScoped<ImportHolidaysHandler>();
        services.AddScoped<ClockRecalculator>();
        services.AddHostedService<LoadCompanyZone>();

        // Event handlers are registered by the interface they answer to (the outbox looks them up that way).
        services.AddScoped<IIntegrationEventHandler<TicketCreated>, StartClockOnTicketCreated>();
        services.AddScoped<IIntegrationEventHandler<FirstAgentReply>, StopResponseOnFirstReply>();
        services.AddScoped<IIntegrationEventHandler<TicketResolved>, StopResolveOnTicketResolved>();
        services.AddScoped<IIntegrationEventHandler<TicketReopened>, RestartResolveOnTicketReopened>();
        services.AddScoped<IIntegrationEventHandler<TicketPending>, PauseClockOnTicketPending>();
        services.AddScoped<IIntegrationEventHandler<TicketResumed>, ResumeClockOnTicketResumed>();
        services.AddScoped<IIntegrationEventHandler<TicketCancelled>, StopClockOnTicketCancelled>();
        services.AddScoped<IIntegrationEventHandler<TicketPriorityChanged>, ChangeTargetsOnPriorityChanged>();
        services.AddScoped<IDomainEventHandler<DeadlinesChangedEvent>, PublishDeadlinesChanged>();
        services.AddScoped<IDomainEventHandler<SlaBreachImminentEvent>, PublishSlaBreachImminent>();
        services.AddScoped<IDomainEventHandler<SlaBreachedEvent>, PublishSlaBreached>();

        // The breach scheduler: warnings and breaches are found by polling the clocks' check times.
        services.AddScoped<SlaScanner>();
        services.AddHostedService<SlaScheduler>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(typeof(SlaModule).Assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(SlaErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapSlaEndpoints(this IEndpointRouteBuilder api)
    {
        var sla = api.MapGroup("").WithTags("Sla");
        GetPoliciesEndpoint.Map(sla);
        ChangePolicyEndpoint.Map(sla);
        GetCalendarEndpoint.Map(sla);
        ChangeCalendarEndpoint.Map(sla);
        HolidayEndpoints.Map(sla);
        ImportHolidaysEndpoints.Map(sla);
        return api;
    }
}
