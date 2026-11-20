using Microsoft.AspNetCore.Identity;

namespace Yasda.Modules.Identity.Infrastructure;

// Identity calls this for "forgot password". It sends straight away, not through the mail queue: the person is
// waiting for the message.
internal sealed class SmtpEmailSender(IMailer mailer) : IEmailSender<AppUser>
{
    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        mailer.SendAsync(email, "Confirm your email", $"Confirm your email address: {confirmationLink}", CancellationToken.None);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        mailer.SendAsync(email, "Reset your password", $"Reset your password: {resetLink}", CancellationToken.None);

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode) =>
        mailer.SendAsync(email, "Your password reset code",
            $"Hello {user.FullName},\n\nYour password reset code is:\n\n{resetCode}\n\n" +
            "Enter it on the sign-in page under \"Forgot password\". If you didn't ask for this, ignore this email.",
            CancellationToken.None);
}
