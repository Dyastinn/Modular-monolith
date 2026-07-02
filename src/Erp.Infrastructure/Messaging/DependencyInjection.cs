using Erp.Application.Abstractions.Messaging;
using Erp.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Infrastructure.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        var applicationAssembly = typeof(IRequestHandler<,>).Assembly;

        var handlerRegistrations =
            from implementationType in applicationAssembly.GetTypes()
            where implementationType is { IsAbstract: false, IsInterface: false }
            from interfaceType in implementationType.GetInterfaces()
            where interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
            select (interfaceType, implementationType);

        foreach (var (interfaceType, implementationType) in handlerRegistrations)
        {
            services.AddTransient(interfaceType, implementationType);
        }

        services.AddSingleton<ISender, Sender>();

        // Registration order = wrapping order: the first behavior registered here is the outermost
        // wrapper around every handler call. Add new behaviors below LoggingBehavior to run inside it.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
