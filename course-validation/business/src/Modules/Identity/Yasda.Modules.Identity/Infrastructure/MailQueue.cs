using Microsoft.EntityFrameworkCore;
using Yasda.Modules.Identity.Contracts;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class MailQueue(YasdaIdentityDbContext db, TimeProvider time) : IMailQueue
{
    public async Task QueueAsync(string key, long toUserId, string subject, string body, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == toUserId, ct);
        if (user is null || user.IsDisabled || string.IsNullOrWhiteSpace(user.Email))
            return;

        var alreadyQueued = await db.OutgoingMails.AnyAsync(m => m.Key == key, ct);
        if (alreadyQueued)
            return;

        db.OutgoingMails.Add(new OutgoingMail(key, user.Email, subject, body, time.GetUtcNow().UtcDateTime));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another delivery queued the same key between our check and our save: that is the outcome we wanted.
            var nowQueued = await db.OutgoingMails.AsNoTracking().AnyAsync(m => m.Key == key, ct);
            if (!nowQueued)
                throw;
        }
    }
}
