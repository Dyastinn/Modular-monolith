using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Shared.Infrastructure;

public static class DependencyInjection
{
    // The platform services every module codes against.
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ErpDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ErpDb is not configured.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IDbConnectionFactory, PostgresConnectionFactory>();
        services.AddScoped<ISharedConnection, SharedPostgresConnection>();

        // PostgreSQL names columns in snake_case (order_number); our DTOs use PascalCase (OrderNumber).
        // This tells Dapper that the two are the same name, for every read query in every module.
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
