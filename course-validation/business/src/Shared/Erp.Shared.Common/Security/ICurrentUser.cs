namespace Erp.Shared.Common;

// An interface any module may depend on to ask "who is calling?" without touching ASP.NET (Phase 4 §8).
// Shared.Infrastructure supplies the implementation that reads HttpContext claims.
public interface ICurrentUser
{
    long UserId { get; }
    string Username { get; }
    string Role { get; }
    bool IsInRole(string role);
}
