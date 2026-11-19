using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record GetTicketQuery(long TicketId);

internal sealed record TicketDetailDto(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason, IReadOnlyList<TicketMessageDto> Messages);

internal sealed record TicketMessageDto(long Id, long AuthorId, string AuthorName, string Body, bool IsInternal, DateTime CreatedUtc);

// One row of tickets.get_ticket, exactly as the SQL function returns it.
internal sealed record TicketRow(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason);

internal sealed class GetTicketHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<TicketDetailDto?> HandleAsync(GetTicketQuery q, CancellationToken ct)
    {
        // Staff see every ticket and the internal notes; anyone else only their own tickets, without the notes.
        var isStaff = TicketRoles.IsHandler(currentUser.Role);
        long? requesterId = isStaff ? null : currentUser.UserId;

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM tickets.get_ticket(@TicketId, @RequesterId);
            SELECT * FROM tickets.get_ticket_messages(@TicketId, @IsStaff);
            """, new { q.TicketId, RequesterId = requesterId, IsStaff = isStaff }, cancellationToken: ct));

        var row = await multi.ReadSingleOrDefaultAsync<TicketRow>();
        if (row is null) return null;
        var messages = (await multi.ReadAsync<TicketMessageDto>()).ToList();

        return new TicketDetailDto(row.Id, row.Subject, row.Status, row.Priority, row.RequesterId, row.RequesterName,
            row.AssigneeId, row.AssigneeName, row.CreatedUtc, row.UpdatedUtc, row.FirstResponseUtc, row.ResolvedUtc,
            row.ClosedUtc, row.CancelReason, messages);
    }
}
