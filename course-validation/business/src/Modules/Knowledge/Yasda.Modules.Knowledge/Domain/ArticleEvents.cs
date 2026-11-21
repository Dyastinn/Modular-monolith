using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Domain;

// Steps of the review that somebody must hear about, in Knowledge's own words. Each is saved to the outbox in the same save as the
// step, then delivered after the commit; the mail handlers turn them into mail. EventId names this one happening: mail is keyed
// by it, so a redelivered event queues each mail once.
internal sealed record ArticleSubmittedEvent(Guid EventId, ArticleId ArticleId, string Title, UserId SubmittedById) : IDomainEvent;

internal sealed record ArticleRejectedEvent(Guid EventId, ArticleId ArticleId, string Title, UserId AuthorId, UserId RejectedById, string Reason) : IDomainEvent;

internal sealed record ArticlePublishedEvent(Guid EventId, ArticleId ArticleId, string Title, UserId AuthorId, UserId PublishedById, int VersionNumber) : IDomainEvent;
