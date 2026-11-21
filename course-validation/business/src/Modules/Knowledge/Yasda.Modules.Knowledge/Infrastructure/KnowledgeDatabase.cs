using System.Data.Common;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Knowledge.Infrastructure;

internal sealed class KnowledgeDatabase(IUserDirectory users, TimeProvider time) : IModuleDatabase
{
    public string Module => "Knowledge";
    public string Schema => KnowledgeDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        await using var db = Open(ownerConnection);
        await db.Database.MigrateAsync(ct);
    }

    // Three articles to click through, built through the domain like real ones: so each one is written by a different person
    // than the one who publishes it. Identity seeds first (registration order), so its sample people exist; their ids come
    // through its contract.
    public async Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        await using var db = Open(ownerConnection);
        if (await db.Articles.AnyAsync(ct)) return;

        var agent = await PersonAsync("agent@yasda.local", ct);
        var lead = await PersonAsync("lead@yasda.local", ct);
        var editor = await PersonAsync("editor@yasda.local", ct);
        if (agent is null || lead is null || editor is null) return;
        var now = time.GetUtcNow().UtcDateTime;
        var nobodyElse = new ArticleAccess(IsEditor: false, LeadsAuthorsTeam: false);

        var password = Article.Create("Reset your password",
            "Forgot your password? Reset it yourself in about two minutes.",
            "Open the sign-in page and choose \"Forgot your password?\".\n\nType the email address you sign in with. We send you a link that works for one hour.\n\nOpen the link, choose a new password, and sign in with it.",
            ArticleVisibility.Public, agent.Value, now.AddDays(-9));
        password.Submit(agent.Value, nobodyElse, now.AddDays(-9));
        password.Publish(editor.Value, actorIsAdmin: false, now.AddDays(-8));

        var outage = Article.Create("Escalating a network outage",
            "When the whole floor loses the network, who to call and what to say.",
            "Check that the switch on the floor has power before you escalate.\n\nThen open a Critical ticket in the Network category and put the floor and the time in the first line.\n\nTell the Network team lead directly. Do not wait for the SLA warning.",
            ArticleVisibility.Internal, lead.Value, now.AddDays(-6));
        outage.Submit(lead.Value, nobodyElse, now.AddDays(-6));
        outage.Publish(editor.Value, actorIsAdmin: false, now.AddDays(-5));

        var vpn = Article.Create("Set up the VPN on a home laptop",
            "Connect your own laptop to the company network from home.",
            "Download the VPN client from the software page.\n\nSign in with your work email and the code from your authenticator app.",
            ArticleVisibility.Public, agent.Value, now.AddDays(-1));

        db.Articles.AddRange(password, outage, vpn);
        await db.SaveChangesAsync(ct);

        // Some reading, so the popular list has something to show.
        var today = time.Today();
        db.Add(new ArticleViewDay { ArticleId = password.Id, Day = today.AddDays(-2), Views = 9 });
        db.Add(new ArticleViewDay { ArticleId = password.Id, Day = today, Views = 3 });
        db.Add(new ArticleViewDay { ArticleId = outage.Id, Day = today.AddDays(-1), Views = 4 });
        await db.SaveChangesAsync(ct);
    }

    private async Task<Person?> PersonAsync(string email, CancellationToken ct)
    {
        var id = await users.IdOfAsync(email, ct);
        if (id is null) return null;
        var name = await users.NameOfAsync(id.Value, ct);
        if (name is null) return null;
        return new Person(new UserId(id.Value), name);
    }

    private static KnowledgeDbContext Open(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
        KnowledgeDbContext.Configure(options, connection);
        return new KnowledgeDbContext(options.Options);
    }
}
