using System.Data;
using Dapper;
using Erp.Shared.Common;
using Erp.Shared.Common.Data;

namespace Erp.Shared.Infrastructure;

// The Dapper ADAPTER for the IRefreshTokenRepository port in Erp.Shared.Common/Security (Phase 4 §9).
public sealed class RefreshTokenRepository(IDbConnectionFactory factory) : IRefreshTokenRepository
{
    public async Task AddAsync(string tokenHash, long userId, Guid familyId, Guid securityStamp,
        DateTime expiresUtc, DateTime absoluteExpiresUtc, IDbTransaction tx, CancellationToken ct) =>
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_Insert",
            new { TokenHash = tokenHash, UserId = userId, FamilyId = familyId, SecurityStamp = securityStamp, ExpiresUtc = expiresUtc, AbsoluteExpiresUtc = absoluteExpiresUtc },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct)
    {
        using var conn = factory.Create();   // read: no transaction needed
        return await conn.QuerySingleOrDefaultAsync<RefreshTokenRecord>(new CommandDefinition(
            "[identity].usp_RefreshToken_GetByHash",
            new { TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task RevokeAsync(string tokenHash, string? replacedByHash, IDbTransaction tx, CancellationToken ct) =>
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_Revoke",
            new { TokenHash = tokenHash, ReplacedByHash = replacedByHash },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public async Task RevokeFamilyAsync(Guid familyId, IDbTransaction tx, CancellationToken ct) =>
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_RevokeFamily",
            new { FamilyId = familyId },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public async Task RevokeAllForUserAsync(long userId, IDbTransaction tx, CancellationToken ct) =>
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_RevokeAllForUser",
            new { UserId = userId },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}
