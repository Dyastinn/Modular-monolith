using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Infrastructure;

public static class DependencyInjection
{
    // The platform every module uses: mediator + behaviors, Dapper, JWT/auth plumbing (Phase 11 §7).
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMessaging();

        services.AddScoped<IDbConnectionFactory>(_ =>
            new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IClock, SystemClock>();                       // testable time (Phase 4 §10)
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>(); // refresh generate/hash/expiry (Phase 4 §10)
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

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
