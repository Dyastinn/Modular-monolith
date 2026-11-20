using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class MailOptions
{
    public int PollIntervalMs { get; set; } = 2000;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 10;
}

// Sends the mail waiting in identity.outgoing_mail, as the outbox dispatcher delivers events: poll, take a batch with
// FOR UPDATE SKIP LOCKED (several app instances never take the same mail), try each, record the result. A failure
// counts an attempt and is retried on the next tick; after MaxAttempts the row stays, with its last_error, for a
// person to look at. One bad mail never blocks the others, and never touches the outbox.
internal sealed class MailSender(
    IServiceScopeFactory scopes,
    IConfiguration config,
    TimeProvider time,
    ILogger<MailSender> log) : BackgroundService
{
    // The host listens to the meter named "Yasda" (Observability.cs), so a meter with that name is found.
    private static readonly Meter YasdaMeter = new("Yasda");
    private static readonly Counter<long> Sent = YasdaMeter.CreateCounter<long>("yasda.mail.sent");
    private static readonly Counter<long> Failed = YasdaMeter.CreateCounter<long>("yasda.mail.failed");

    private readonly MailOptions _options = config.GetSection("Mail").Get<MailOptions>() ?? new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // A full batch means more may be waiting, so go again until a batch comes back short.
                var moreWaiting = true;
                while (moreWaiting)
                {
                    var handled = await SendBatchAsync(stoppingToken);
                    moreWaiting = handled == _options.BatchSize;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Sending queued mail failed; will retry next tick");
            }
        }
    }

    public async Task<int> SendBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<YasdaIdentityDbContext>();
        var mailer = scope.ServiceProvider.GetRequiredService<IMailer>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutgoingMails
            .FromSqlInterpolated($"""
                SELECT * FROM identity.outgoing_mail
                WHERE sent_utc IS NULL AND attempts < {_options.MaxAttempts}
                ORDER BY queued_utc
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var mail in batch)
        {
            try
            {
                await mailer.SendAsync(mail.ToAddress, mail.Subject, mail.Body, ct);
                mail.MarkSent(time.GetUtcNow().UtcDateTime);
                Sent.Add(1);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                mail.MarkFailed(ex.Message);
                Failed.Add(1);
                log.LogError(ex, "Mail {Key} failed (attempt {Attempts})", mail.Key, mail.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
