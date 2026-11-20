using System.Data.Common;
using Yasda.Modules.Identity.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class IdentityDatabase(UserManager<AppUser> users, TimeProvider time) : IModuleDatabase
{
    public const string DevPassword = "Password123!";

    public string Module => "Identity";
    public string Schema => YasdaIdentityDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<YasdaIdentityDbContext>();
        YasdaIdentityDbContext.Configure(options, ownerConnection);
        await using var db = new YasdaIdentityDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
    }

    // Through UserManager rather than SQL, so passwords are hashed exactly as in real use.
    public async Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        (string Email, string Name, string Role)[] devUsers =
        [
            ("admin@yasda.local", "Ada Mendoza", Roles.Admin),
            ("lead@yasda.local", "Lea Torres", Roles.TeamLead),
            ("agent@yasda.local", "Alan Reyes", Roles.Agent),
            ("editor@yasda.local", "Kira Santos", Roles.KbEditor),
            ("requester@yasda.local", "Rita Cruz", Roles.Requester),
        ];

        foreach (var (email, name, role) in devUsers)
        {
            if (await users.FindByEmailAsync(email) is not null) continue;
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name };
            ThrowIfFailed(await users.CreateAsync(user, DevPassword));
            ThrowIfFailed(await users.AddToRoleAsync(user, role));
        }

        await SeedTeamsAsync(ownerConnection, ct);
    }

    // Two teams to route tickets to: the service desk (an agent and a team lead) and Network (a team lead).
    private async Task SeedTeamsAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<YasdaIdentityDbContext>();
        YasdaIdentityDbContext.Configure(options, ownerConnection);
        await using var db = new YasdaIdentityDbContext(options.Options);
        if (await db.Teams.AnyAsync(ct)) return;

        var agent = await users.FindByEmailAsync("agent@yasda.local");
        var lead = await users.FindByEmailAsync("lead@yasda.local");
        if (agent is null || lead is null) return;
        var now = time.GetUtcNow().UtcDateTime;

        var serviceDesk = Team.Create("Service desk", nameTaken: false, now);
        serviceDesk.AddMember(new UserId(agent.Id), userIsStaff: true, now);
        serviceDesk.AddMember(new UserId(lead.Id), userIsStaff: true, now);
        var network = Team.Create("Network", nameTaken: false, now);
        network.AddMember(new UserId(lead.Id), userIsStaff: true, now);

        db.Teams.AddRange(serviceDesk, network);
        await db.SaveChangesAsync(ct);
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
