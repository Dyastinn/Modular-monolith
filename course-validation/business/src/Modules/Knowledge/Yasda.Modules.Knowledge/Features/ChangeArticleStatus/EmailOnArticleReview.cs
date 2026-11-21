using Microsoft.Extensions.Configuration;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

// Mail about the review (decision E9). These run in the outbox delivery, so they only queue: Identity's background service sends.
// The key of each mail is "kind:eventId:userId", so a redelivered event queues each mail once. Nobody gets mail about their own action.

internal static class ArticleMail
{
    // Where the article can be opened. App:PublicUrl is an installation setting.
    public static string BaseUrl(IConfiguration config)
    {
        return (config["App:PublicUrl"] ?? "").TrimEnd('/');
    }
}

// Someone sent an article for review: the KbEditors hear. An installation with no KbEditor has only its Admins to publish, so they hear.
internal sealed class EmailOnArticleSubmitted(IMailQueue mail, IUserDirectory users, IConfiguration config) : IDomainEventHandler<ArticleSubmittedEvent>
{
    public async Task HandleAsync(ArticleSubmittedEvent domainEvent, CancellationToken ct)
    {
        var reviewers = await users.UsersInRoleAsync(Roles.KbEditor, ct);
        if (reviewers.Count == 0) reviewers = await users.UsersInRoleAsync(Roles.Admin, ct);

        var subject = $"Article waiting for review: {domainEvent.Title}";
        var body = $"{domainEvent.Title}\n\nIt needs a review before readers can see it.\n\nOpen it: {ArticleMail.BaseUrl(config)}/kb/manage/{domainEvent.ArticleId.Value}";
        foreach (var person in reviewers)
        {
            if (person == domainEvent.SubmittedById.Value) continue;
            await mail.QueueAsync($"article-submitted:{domainEvent.EventId}:{person}", person, subject, body, ct);
        }
    }
}

// A reviewer sent the article back: the author hears why.
internal sealed class EmailOnArticleRejected(IMailQueue mail, IConfiguration config) : IDomainEventHandler<ArticleRejectedEvent>
{
    public async Task HandleAsync(ArticleRejectedEvent domainEvent, CancellationToken ct)
    {
        if (domainEvent.AuthorId == domainEvent.RejectedById) return;

        var subject = $"Your article was sent back: {domainEvent.Title}";
        var body = $"{domainEvent.Title}\n\nWhy: {domainEvent.Reason}\n\nChange it and send it again: {ArticleMail.BaseUrl(config)}/kb/manage/{domainEvent.ArticleId.Value}";
        var author = domainEvent.AuthorId.Value;
        await mail.QueueAsync($"article-rejected:{domainEvent.EventId}:{author}", author, subject, body, ct);
    }
}

// A reviewer published the article: the author hears it is live.
internal sealed class EmailOnArticlePublished(IMailQueue mail, IConfiguration config) : IDomainEventHandler<ArticlePublishedEvent>
{
    public async Task HandleAsync(ArticlePublishedEvent domainEvent, CancellationToken ct)
    {
        if (domainEvent.AuthorId == domainEvent.PublishedById) return;

        var subject = $"Your article is published: {domainEvent.Title}";
        var body = $"{domainEvent.Title}\n\nVersion {domainEvent.VersionNumber} is what readers see now.\n\nOpen it: {ArticleMail.BaseUrl(config)}/kb/{domainEvent.ArticleId.Value}";
        var author = domainEvent.AuthorId.Value;
        await mail.QueueAsync($"article-published:{domainEvent.EventId}:{author}", author, subject, body, ct);
    }
}
