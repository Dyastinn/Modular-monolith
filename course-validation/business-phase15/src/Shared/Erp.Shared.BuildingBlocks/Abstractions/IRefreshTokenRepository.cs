using System.Data;

namespace Erp.Shared.BuildingBlocks;

// The stored refresh token, as the handler needs it (Phase 4 §10).
public sealed record RefreshTokenRecord(
    long Id, string TokenHash, long UserId, Guid FamilyId, Guid SecurityStamp,
    DateTime ExpiresUtc, DateTime AbsoluteExpiresUtc, DateTime? RevokedUtc);

// The PORT — same home as IUserRepository. Shared.Infrastructure supplies the Dapper adapter.
public interface IRefreshTokenRepository
{
    Task AddAsync(string tokenHash, long userId, Guid familyId, Guid securityStamp,
        DateTime expiresUtc, DateTime absoluteExpiresUtc, IDbTransaction tx, CancellationToken ct);
    Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAsync(string tokenHash, string? replacedByHash, IDbTransaction tx, CancellationToken ct);
    Task RevokeFamilyAsync(Guid familyId, IDbTransaction tx, CancellationToken ct);
    Task RevokeAllForUserAsync(long userId, IDbTransaction tx, CancellationToken ct);
}
