using Dapper;
using FluentValidation;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Assigned: "me" (my queue), "unassigned" (nobody's yet), or empty for all. A requester only ever gets their own.
internal sealed record SearchTicketsQuery(string? Status = null, string? Priority = null, string? Assigned = null, string? Search = null,
    string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record TicketSummaryDto(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc);

internal sealed class SearchTicketsValidator : AbstractValidator<SearchTicketsQuery>
{
    public SearchTicketsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("id", "subject", "status", "priority", "requesterName", "assigneeName", "createdUtc", "updatedUtc");
        RuleFor(x => x.Status).IsEnumName(typeof(TicketStatus), caseSensitive: true).When(x => x.Status is not null);
        RuleFor(x => x.Priority).IsEnumName(typeof(TicketPriority), caseSensitive: true).When(x => x.Priority is not null);
        RuleFor(x => x.Assigned).Must(a => a is null or "me" or "unassigned").WithMessage("Assigned is \"me\", \"unassigned\" or empty.");
    }
}

internal sealed class SearchTicketsHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<TicketSummaryDto>> HandleAsync(SearchTicketsQuery q, CancellationToken ct)
    {
        // A requester only ever sees their own tickets, whatever the query asks for.
        long? requesterId = null;
        if (!TicketRoles.IsHandler(currentUser.Role)) requesterId = currentUser.UserId;

        long? assigneeId = null;
        if (q.Assigned == "me") assigneeId = currentUser.UserId;
        var unassignedOnly = q.Assigned == "unassigned";

        var args = new
        {
            RequesterId = requesterId, q.Status, q.Priority, AssigneeId = assigneeId, Unassigned = unassignedOnly,
            Search = SearchText.ForLike(q.Search), q.Sort, q.Desc, q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM tickets.search_tickets(@RequesterId, @Status, @Priority, @AssigneeId, @Unassigned, @Search, @Sort, @Desc, @Page, @PageSize);
            SELECT tickets.count_tickets(@RequesterId, @Status, @Priority, @AssigneeId, @Unassigned, @Search);
            """, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<TicketSummaryDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<TicketSummaryDto>(items, q.Page, q.PageSize, total);
    }
}
