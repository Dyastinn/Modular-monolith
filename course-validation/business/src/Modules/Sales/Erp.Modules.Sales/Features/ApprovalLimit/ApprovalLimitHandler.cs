using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ApprovalLimitDto(decimal ApprovalThreshold, long? UpdatedBy, DateTime? UpdatedUtc);

internal sealed class GetApprovalLimitHandler(IDbConnectionFactory connections)
{
    public async Task<ApprovalLimitDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<ApprovalLimitDto>(new CommandDefinition(
            "SELECT * FROM sales.get_sales_settings();", cancellationToken: ct));
    }
}

internal sealed record SetApprovalLimitCommand(decimal ApprovalThreshold);

internal sealed class SetApprovalLimitHandler(SalesDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task HandleAsync(SetApprovalLimitCommand cmd, CancellationToken ct)
    {
        var settings = await db.SettingsAsync(ct);
        settings.SetApprovalThreshold(new Money(cmd.ApprovalThreshold), new UserId(currentUser.UserId), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}
