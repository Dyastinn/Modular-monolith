using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "yasda@localhost";
}

// Sends one mail. A class behind an interface so tests can swap in one that fails.
internal interface IMailer
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

// With no SMTP host configured (development), the message is logged instead, so every flow can still be followed.
internal sealed class SmtpMailer(IConfiguration config, ILogger<SmtpMailer> log) : IMailer
{
    private readonly SmtpOptions _smtp = config.GetSection("Smtp").Get<SmtpOptions>() ?? new();

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host))
        {
            log.LogWarning("Smtp:Host is not set; email to {To} not sent. Subject: {Subject}. Body: {Body}", to, subject, body);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrEmpty(_smtp.Username))
            await client.AuthenticateAsync(_smtp.Username, _smtp.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
