using System.Data;

namespace Erp.Shared.BuildingBlocks;

// A plain read shape — deliberately NOT a rich domain aggregate, because authentication is a platform
// concern, not a Sales business rule (Phase 4 §5).
public sealed record AppUser(
    long Id, string Username, string PasswordHash, string Role, bool IsActive,
    Guid SecurityStamp,          // rotates on every password change (Phase 3 schema)
    DateTime PasswordExpiresUtc); // DB-driven 90-day policy; caps a refresh token's lifetime

// The PORT lives here in Common, next to ICurrentUser and IDbConnectionFactory; Shared.Infrastructure
// supplies the Dapper ADAPTER (UserRepository). Phase 4 §5.
public interface IUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<AppUser?> GetByIdAsync(long id, CancellationToken ct);   // used by refresh to reload role/active (Phase 4 §10)
    Task ChangePasswordAsync(long userId, string newPasswordHash, IDbTransaction tx, CancellationToken ct);  // Phase 4 Part 4
}
