using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Erp.Shared.Common;

namespace Erp.Shared.Infrastructure;

// Reads the validated token's claims off HttpContext to fulfil the ICurrentUser promise (Phase 4 §8).
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context.");

    public long UserId => long.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    public string Username => User.FindFirstValue(ClaimTypes.Name)!;
    public string Role => User.FindFirstValue(ClaimTypes.Role)!;
    public bool IsInRole(string role) => User.IsInRole(role);
}
