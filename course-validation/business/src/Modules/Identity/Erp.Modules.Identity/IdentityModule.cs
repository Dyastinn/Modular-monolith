using Carter;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Identity;

// The module's one public type: how the host plugs it in.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(IdentityModule).Assembly;

        services.AddDbContext<IdentityDbContext>((sp, o) =>
            IdentityDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection));
        services.AddScoped<IModuleDatabase, IdentityDatabase>();

        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<RefreshTokenService>();
        services.AddSingleton<JwtTokenService>();

        // Carter's AddCarter() only discovers PUBLIC endpoint classes; ours are internal, so register them here
        // or every /auth route silently 404s.
        foreach (var endpoint in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)))
            services.AddSingleton(typeof(ICarterModule), endpoint);

        // includeInternalTypes: true is REQUIRED — without it no validator is found and none ever runs, silently.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
