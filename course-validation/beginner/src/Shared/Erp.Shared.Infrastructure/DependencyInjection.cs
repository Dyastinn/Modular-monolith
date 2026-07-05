using Carter;
using Erp.Shared.Infrastructure.Persistence;
using Erp.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMessaging();
        services.AddCarter();
        services.AddScoped<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<ISender, Sender>();
        services.AddScoped<IPublisher, Publisher>();
        // shared behaviors, in wrap order (first registered = outermost)
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));    // outermost
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)); // inside logging
        return services;
    }
}
