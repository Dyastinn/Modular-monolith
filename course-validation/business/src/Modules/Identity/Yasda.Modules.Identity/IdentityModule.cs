using System.Security.Cryptography.X509Certificates;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Identity.Domain;
using Yasda.Modules.Identity.Features;
using Yasda.Modules.Identity.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Modules.Identity;

// Sign-in is ASP.NET Core Identity: password hashing, lockout, cookies for the browser, bearer + refresh tokens
// for other clients. This module only configures it and adds what it lacks.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<YasdaIdentityDbContext>((sp, o) =>
            YasdaIdentityDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IModuleDatabase, IdentityDatabase>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IDomainEventHandler<TeamRenamedEvent>, PublishTeamRenamed>();
        services.AddScoped<ITeamDirectory, TeamDirectory>();

        services.AddIdentityApiEndpoints<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                // Length beats composition rules (NIST SP 800-63B).
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<long>>()
            .AddEntityFrameworkStores<YasdaIdentityDbContext>()
            .AddSignInManager<YasdaSignInManager>();

        services.AddTransient<IEmailSender<AppUser>, SmtpEmailSender>();
        services.AddSingleton<IMailer, SmtpMailer>();
        services.AddScoped<IMailQueue, MailQueue>();
        services.AddHostedService<MailSender>();
        // How soon a disabled account or a changed role ends a browser session (each check is one query).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        var auth = config.GetSection("Auth");
        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o =>
        {
            o.BearerTokenExpiration = TimeSpan.FromMinutes(auth.GetValue("AccessTokenMinutes", 15));
            o.RefreshTokenExpiration = TimeSpan.FromDays(auth.GetValue("RefreshTokenDays", 7));
        });
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromHours(auth.GetValue("CookieHours", 8));
            o.SlidingExpiration = true;
            // An API answers 401/403; it never redirects to a login page.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Yasda")
            .PersistKeysToDbContext<YasdaIdentityDbContext>();

        // The keys live in a table the app can read. Encrypted with a certificate, a copy of that table alone
        // (a backup, a stolen dump) can't be used to forge a sign-in.
        var certificatePath = config["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificatePath, config["DataProtection:CertificatePassword"]));
        else if (config.GetValue("DataProtection:RequireCertificate", true))
            throw new InvalidOperationException(
                "Set DataProtection:CertificatePath and DataProtection:CertificatePassword: sign-in keys must be encrypted at rest.");

        services.AddHostedService<InitialAdmin>();
        services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(IdentityErrors)));

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<GetCurrentUserHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<SignOutHandler>();
        services.AddScoped<SignOutEverywhereHandler>();
        services.AddScoped<CreateUserHandler>();
        services.AddScoped<SearchUsersHandler>();
        services.AddScoped<GetUserHandler>();
        services.AddScoped<ListStaffHandler>();
        services.AddScoped<UpdateUserHandler>();
        services.AddScoped<SetUserDisabledHandler>();
        services.AddScoped<ResetUserPasswordHandler>();
        services.AddScoped<SearchSignInAttemptsHandler>();
        services.AddScoped<SearchTeamsHandler>();
        services.AddScoped<GetTeamHandler>();
        services.AddScoped<CreateTeamHandler>();
        services.AddScoped<RenameTeamHandler>();
        services.AddScoped<SetTeamActiveHandler>();
        services.AddScoped<SetTeamNotifyHandler>();
        services.AddScoped<AddTeamMemberHandler>();
        services.AddScoped<RemoveTeamMemberHandler>();

        return services;
    }

    // /auth/login, /auth/refresh, /auth/forgotPassword, /auth/resetPassword, /auth/manage/info, ...
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/auth")
            .MapIdentityApi<AppUser>()
            .AddEndpointFilter(async (ctx, next) =>
            {
                // Staff accounts are created by an administrator, never by self sign-up.
                var path = ctx.HttpContext.Request.Path.Value ?? "";
                if (path.EndsWith("/auth/register", StringComparison.OrdinalIgnoreCase))
                    return TypedResults.NotFound();
                return await next(ctx);
            })
            .RequireRateLimiting(RateLimitPolicies.Auth);

        var identity = app.MapGroup("").WithTags("Identity");
        GetCurrentUserEndpoint.Map(identity);
        ChangePasswordEndpoint.Map(identity);
        SignOutEndpoint.Map(identity);
        CreateUserEndpoint.Map(identity);
        SearchUsersEndpoint.Map(identity);
        GetUserEndpoint.Map(identity);
        ListStaffEndpoint.Map(identity);
        UpdateUserEndpoint.Map(identity);
        SearchSignInAttemptsEndpoint.Map(identity);
        SearchTeamsEndpoint.Map(identity);
        GetTeamEndpoint.Map(identity);
        CreateTeamEndpoint.Map(identity);
        UpdateTeamEndpoint.Map(identity);
        TeamMembersEndpoint.Map(identity);
        return app;
    }
}
