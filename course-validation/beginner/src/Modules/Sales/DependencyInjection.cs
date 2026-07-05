using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // Scan this module's own assembly for its request handlers and register each.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            {
                services.AddScoped(handlerInterface, type);
            }

            // Register FluentValidation validators as IValidator<T>.
            foreach (var validatorInterface in type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddScoped(validatorInterface, type);
            }

            // Register notification handlers (INotificationHandler<T>).
            foreach (var notifInterface in type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
            {
                services.AddScoped(notifInterface, type);
            }
        }

        // This module's Infrastructure (repositories + read ports).
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IPricingService, PricingService>();

        // Teach Dapper how to read/write the strongly-typed IDs (registered once at startup).
        SqlMapper.AddTypeHandler(new SalesOrderIdTypeHandler());
        SqlMapper.AddTypeHandler(new CustomerIdTypeHandler());
        SqlMapper.AddTypeHandler(new ProductIdTypeHandler());

        return services;
    }
}
