using System.Data.Common;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class TicketsDatabase(IUserDirectory users, TimeProvider time) : IModuleDatabase
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
        if (await db.Tickets.AnyAsync(ct)) return;

        var requester = await users.IdOfAsync("requester@yasda.local", ct);
        var agent = await users.IdOfAsync("agent@yasda.local", ct);
        if (requester is null || agent is null) return;
        var requesterName = (await users.NameOfAsync(requester.Value, ct))!;
        var agentName = (await users.NameOfAsync(agent.Value, ct))!;
        var r = new UserId(requester.Value);
        var a = new UserId(agent.Value);
        var now = time.GetUtcNow().UtcDateTime;

        var printer = Ticket.Create("Printer on the 3rd floor won't print",
            "It shows \"paper jam\" but there's no paper stuck. Twenty people share it.", TicketPriority.High, r, requesterName, now.AddHours(-30));
        var vpn = Ticket.Create("Can't connect to the VPN from home",
            "It says my certificate expired. I need it for Monday's report.", TicketPriority.Normal, r, requesterName, now.AddHours(-6));
        vpn.Take(a, agentName, now.AddHours(-5));
        vpn.Reply(a, agentName, "Which computer are you using: the company laptop or your own?", isInternal: false, authorIsStaff: true, now.AddHours(-5));
        vpn.Reply(a, agentName, "Certificates were renewed last week; this may be the first of many.", isInternal: true, authorIsStaff: true, now.AddHours(-5));
        vpn.MarkPending(now.AddHours(-5));
        var mouse = Ticket.Create("New mouse, please", "Mine's scroll wheel stopped working.", TicketPriority.Low, r, requesterName, now.AddDays(-3));
        mouse.Take(a, agentName, now.AddDays(-3));
        mouse.Reply(a, agentName, "Left one on your desk. Shout if it doesn't work.", isInternal: false, authorIsStaff: true, now.AddDays(-2));
        mouse.Resolve(now.AddDays(-2));

        db.Tickets.AddRange(printer, vpn, mouse);
        await db.SaveChangesAsync(ct);
    }

    private static TicketsDbContext Open(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<TicketsDbContext>();
        TicketsDbContext.Configure(options, connection);
        return new TicketsDbContext(options.Options);
    }
}
