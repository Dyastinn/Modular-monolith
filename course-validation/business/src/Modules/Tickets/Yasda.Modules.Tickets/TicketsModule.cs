using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Features;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Modules.Tickets;

public static class TicketsModule
{
    public static IServiceCollection AddTicketsModule(this IServiceCollection services)
    {
        services.AddDbContext<TicketsDbContext>((sp, o) =>
            TicketsDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, TicketsDatabase>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<CreateTicketHandler>();
        services.AddScoped<SearchTicketsHandler>();
        services.AddScoped<GetTicketHandler>();
        services.AddScoped<TakeTicketHandler>();
        services.AddScoped<AssignTicketHandler>();
        services.AddScoped<ReplyToTicketHandler>();
        services.AddScoped<ChangeTicketStatusHandler>();
        services.AddScoped<ChangePriorityHandler>();
        services.AddScoped<RouteTicketHandler>();
        services.AddScoped<SearchCategoriesHandler>();
        services.AddScoped<GetCategoryHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<UpdateCategoryHandler>();

        // Event handlers are registered by the interface they answer to (the outbox looks them up that way).
        services.AddScoped<IDomainEventHandler<TicketCreatedEvent>, PublishTicketCreated>();
        services.AddScoped<IDomainEventHandler<FirstAgentReplyEvent>, PublishFirstAgentReply>();
        services.AddScoped<IDomainEventHandler<TicketResolvedEvent>, PublishTicketResolved>();
        services.AddScoped<IDomainEventHandler<TicketReopenedEvent>, PublishTicketReopened>();
        services.AddScoped<IDomainEventHandler<TicketCancelledEvent>, PublishTicketCancelled>();
        services.AddScoped<IDomainEventHandler<TicketPriorityChangedEvent>, PublishTicketPriorityChanged>();
        services.AddScoped<IDomainEventHandler<TicketPendingEvent>, PublishTicketPending>();
        services.AddScoped<IDomainEventHandler<TicketResumedEvent>, PublishTicketResumed>();
        services.AddScoped<IDomainEventHandler<TicketCreatedEvent>, EmailOnNewTicket>();
        services.AddScoped<IDomainEventHandler<TicketAssignedEvent>, EmailOnAssignment>();
        services.AddScoped<IDomainEventHandler<ReplyAddedEvent>, EmailOnReply>();
        services.AddScoped<IIntegrationEventHandler<DeadlinesChanged>, StoreDeadlines>();
        services.AddScoped<IIntegrationEventHandler<SlaBreachImminent>, EmailOnSlaWarning>();
        services.AddScoped<IIntegrationEventHandler<SlaBreached>, EmailOnSlaBreach>();
        services.AddScoped<IIntegrationEventHandler<TeamRenamed>, RenameTeamCopies>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(typeof(TicketsModule).Assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(TicketsErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapTicketsEndpoints(this IEndpointRouteBuilder api)
    {
        var tickets = api.MapGroup("").WithTags("Tickets");
        CreateTicketEndpoint.Map(tickets);
        SearchTicketsEndpoint.Map(tickets);
        GetTicketEndpoint.Map(tickets);
        TakeTicketEndpoint.Map(tickets);
        AssignTicketEndpoint.Map(tickets);
        ReplyToTicketEndpoint.Map(tickets);
        ChangeTicketStatusEndpoint.Map(tickets);
        ChangePriorityEndpoint.Map(tickets);
        RouteTicketEndpoint.Map(tickets);
        SearchCategoriesEndpoint.Map(tickets);
        GetCategoryEndpoint.Map(tickets);
        CreateCategoryEndpoint.Map(tickets);
        UpdateCategoryEndpoint.Map(tickets);
        return api;
    }
}
