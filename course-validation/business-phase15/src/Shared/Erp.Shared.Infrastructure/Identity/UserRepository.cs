using System.Data;
using Dapper;
using Erp.Shared.Common;
using Erp.Shared.Common.Data;

namespace Erp.Shared.Infrastructure;

// The Dapper ADAPTER for the IUserRepository port declared in Erp.Shared.Common/Security (Phase 4 §5).
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
