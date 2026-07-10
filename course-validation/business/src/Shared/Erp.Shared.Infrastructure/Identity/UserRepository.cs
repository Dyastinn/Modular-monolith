using System.Data;
using Dapper;
using Erp.Shared.Common.Data;

namespace Erp.Shared.Infrastructure;

// A plain read shape — deliberately NOT a rich domain aggregate, because authentication is a platform
// concern, not a Sales business rule (Phase 4 §5).
public sealed record AppUser(
    long Id, string Username, string PasswordHash, string Role, bool IsActive,
    Guid SecurityStamp,          // rotates on every password change (Phase 3 schema)
    DateTime PasswordExpiresUtc); // DB-driven 90-day policy; caps a refresh token's lifetime

public interface IUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<AppUser?> GetByIdAsync(long id, CancellationToken ct);   // used by refresh to reload role/active (Phase 4 §9)
    Task ChangePasswordAsync(long userId, string newPasswordHash, IDbTransaction tx, CancellationToken ct);  // change/reset (Phase 13)
}

public sealed class UserRepository(IDbConnectionFactory factory) : IUserRepository
{
    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        using var conn = factory.Create();
        // [identity] is bracketed — it's a T-SQL reserved keyword (Phase 3 ⚠️).
        return await conn.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            "[identity].usp_AppUser_GetByUsername",
            new { Username = username },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<AppUser?> GetByIdAsync(long id, CancellationToken ct)
    {
        using var conn = factory.Create();
        return await conn.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            "[identity].usp_AppUser_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    // WRITE — runs on the handler's transaction. The proc rotates PasswordHash + SecurityStamp +
    // PasswordExpiresUtc atomically; the new stamp invalidates every outstanding refresh token (global logout).
    public async Task ChangePasswordAsync(long userId, string newPasswordHash, IDbTransaction tx, CancellationToken ct) =>
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_AppUser_ChangePassword",
            new { UserId = userId, NewPasswordHash = newPasswordHash },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}
