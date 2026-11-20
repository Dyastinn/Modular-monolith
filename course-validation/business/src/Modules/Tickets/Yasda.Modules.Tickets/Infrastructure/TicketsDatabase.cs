using System.Data.Common;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class TicketsDatabase(IUserDirectory users, ITeamDirectory teams, TimeProvider time, IEnumerable<IInterceptor> interceptors) : IModuleDatabase
{
    public string Module => "Tickets";
    public string Schema => TicketsDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        await using var db = Open(ownerConnection);
        await db.Database.MigrateAsync(ct);
    }

    // A few tickets to click through, built through the domain like real ones. Identity seeds first (registration
    // order), so its sample people exist; their ids come through Identity's contract, never its tables.
    public async Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        await using var db = Open(ownerConnection);
        await SeedCategoriesAsync(db, ct);
        if (await db.Tickets.AnyAsync(ct)) return;

        var requester = await users.IdOfAsync("requester@yasda.local", ct);
        var agent = await users.IdOfAsync("agent@yasda.local", ct);
        if (requester is null || agent is null) return;
        var requesterName = (await users.NameOfAsync(requester.Value, ct))!;
        var agentName = (await users.NameOfAsync(agent.Value, ct))!;
        var r = new UserId(requester.Value);
        var a = new UserId(agent.Value);
        var now = time.GetUtcNow().UtcDateTime;

        var printer = Ticket.Create(await db.NextTicketIdAsync(ct), "Printer on the 3rd floor won't print",
            "It shows \"paper jam\" but there's no paper stuck. Twenty people share it.", TicketPriority.High, r, requesterName, now.AddHours(-30));
        var vpn = Ticket.Create(await db.NextTicketIdAsync(ct), "Can't connect to the VPN from home",
            "It says my certificate expired. I need it for Monday's report.", TicketPriority.Normal, r, requesterName, now.AddHours(-6));
        vpn.Take(a, agentName, now.AddHours(-5));
        vpn.Reply(a, agentName, "Which computer are you using: the company laptop or your own?", isInternal: false, authorIsStaff: true, now.AddHours(-5));
        vpn.Reply(a, agentName, "Certificates were renewed last week; this may be the first of many.", isInternal: true, authorIsStaff: true, now.AddHours(-5));
        vpn.MarkPending(now.AddHours(-5));
        var mouse = Ticket.Create(await db.NextTicketIdAsync(ct), "New mouse, please", "Mine's scroll wheel stopped working.", TicketPriority.Low, r, requesterName, now.AddDays(-3));
        mouse.Take(a, agentName, now.AddDays(-3));
        mouse.Reply(a, agentName, "Left one on your desk. Shout if it doesn't work.", isInternal: false, authorIsStaff: true, now.AddDays(-2));
        mouse.Resolve(now.AddDays(-2));

        db.Tickets.AddRange(printer, vpn, mouse);
        await db.SaveChangesAsync(ct);
    }

    // The kinds of request, each routed to the team that handles it. Identity seeds first, so its teams exist; their numbers come
    // through its contract. The seeded tickets stay uncategorized, to show what "Not sure" looks like.
    private async Task SeedCategoriesAsync(TicketsDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct)) return;

        var serviceDesk = await TeamAsync("Service desk", ct);
        var network = await TeamAsync("Network", ct);
        if (serviceDesk is null || network is null) return;
        var now = time.GetUtcNow().UtcDateTime;

        db.Categories.AddRange(
            Category.Create("Hardware", nameTaken: false, serviceDesk, now),
            Category.Create("Software", nameTaken: false, serviceDesk, now),
            Category.Create("Access", nameTaken: false, serviceDesk, now),
            Category.Create("Network", nameTaken: false, network, now));
        await db.SaveChangesAsync(ct);
    }

    private async Task<TeamRef?> TeamAsync(string name, CancellationToken ct)
    {
        var id = await teams.TeamIdOfAsync(name, ct);
        if (id is null) return null;
        var info = await teams.FindTeamAsync(id.Value, ct);
        if (info is null) return null;
        return new TeamRef(new TeamId(info.Id), info.Name, info.IsActive);
    }

    // The seed saves through the same interceptors as the app, so seeded tickets write outbox rows and get clocks too.
    private TicketsDbContext Open(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<TicketsDbContext>();
        TicketsDbContext.Configure(options, connection);
        options.AddInterceptors(interceptors);
        return new TicketsDbContext(options.Options);
    }
}
