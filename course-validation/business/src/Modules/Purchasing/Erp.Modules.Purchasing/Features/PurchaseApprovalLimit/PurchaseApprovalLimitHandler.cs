using Dapper;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Features;

internal sealed record PurchaseApprovalLimitDto(decimal ApprovalThreshold, long? UpdatedBy, DateTime? UpdatedUtc);

internal sealed class GetPurchaseApprovalLimitHandler(IDbConnectionFactory connections)
{
    public async Task<PurchaseApprovalLimitDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<PurchaseApprovalLimitDto>(new CommandDefinition(
            "SELECT * FROM purchasing.get_purchasing_settings();", cancellationToken: ct));
    }
}

internal sealed record SetPurchaseApprovalLimitCommand(decimal ApprovalThreshold);

internal sealed class SetPurchaseApprovalLimitHandler(PurchasingDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task HandleAsync(SetPurchaseApprovalLimitCommand cmd, CancellationToken ct)
    {
        var settings = await db.SettingsAsync(ct);
        settings.SetApprovalThreshold(new Money(cmd.ApprovalThreshold), new UserId(currentUser.UserId), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}
