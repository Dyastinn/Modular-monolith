using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Features;
using Yasda.Modules.Knowledge.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Modules.Knowledge;

public static class KnowledgeModule
{
    public static IServiceCollection AddKnowledgeModule(this IServiceCollection services)
    {
        services.AddDbContext<KnowledgeDbContext>((sp, o) =>
            KnowledgeDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, KnowledgeDatabase>();

        // The person making the request, as an article needs to know them.
        services.AddScoped<ActingUser>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<CreateArticleHandler>();
        services.AddScoped<UpdateArticleHandler>();
        services.AddScoped<ChangeArticleStatusHandler>();
        services.AddScoped<AddArticleEditorHandler>();
        services.AddScoped<RemoveArticleEditorHandler>();
        services.AddScoped<SearchArticlesHandler>();
        services.AddScoped<GetArticleHandler>();
        services.AddScoped<BrowsePublishedArticlesHandler>();
        services.AddScoped<GetPublishedArticleHandler>();
        services.AddScoped<PopularArticlesHandler>();
        services.AddScoped<RecordArticleViewHandler>();

        // Event handlers are registered by the interface they answer to (the outbox looks them up that way).
        services.AddScoped<IDomainEventHandler<ArticleSubmittedEvent>, EmailOnArticleSubmitted>();
        services.AddScoped<IDomainEventHandler<ArticleRejectedEvent>, EmailOnArticleRejected>();
        services.AddScoped<IDomainEventHandler<ArticlePublishedEvent>, EmailOnArticlePublished>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(typeof(KnowledgeModule).Assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(KnowledgeErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder api)
    {
        var knowledge = api.MapGroup("").WithTags("Knowledge");
        CreateArticleEndpoint.Map(knowledge);
        UpdateArticleEndpoint.Map(knowledge);
        ChangeArticleStatusEndpoint.Map(knowledge);
        ArticleEditorsEndpoint.Map(knowledge);
        SearchArticlesEndpoint.Map(knowledge);
        GetArticleEndpoint.Map(knowledge);
        BrowsePublishedArticlesEndpoint.Map(knowledge);
        GetPublishedArticleEndpoint.Map(knowledge);
        PopularArticlesEndpoint.Map(knowledge);
        RecordArticleViewEndpoint.Map(knowledge);
        return api;
    }
}
