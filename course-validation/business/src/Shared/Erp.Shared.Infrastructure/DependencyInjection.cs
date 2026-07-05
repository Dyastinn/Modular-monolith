using Erp.Shared.Common;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Infrastructure;

public static class DependencyInjection
{
    // The platform every module uses: mediator + behaviors, Dapper/UoW, JWT/auth plumbing (Phase 11 §7).
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMessaging();

        services.AddScoped<IDbConnectionFactory>(_ =>
            new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    // Shared engine: the Sender + the cross-cutting behaviors. Behaviors run in registration order —
    // Logging OUTERMOST, then Validation (Phase 2).
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<ISender, Sender>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
