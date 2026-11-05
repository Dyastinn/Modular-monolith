using System.Security.Claims;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Http;

namespace Erp.Shared.Infrastructure;

// Reads the authenticated user's claims off HttpContext to fulfil ICurrentUser.
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context.");

    public long UserId => long.Parse(User.FindFirstValue("sub")!);   // "sub" = subject = the user's id
    public string Username => User.FindFirstValue(ClaimTypes.Name)!;
    public string Role => User.FindFirstValue(ClaimTypes.Role)!;
    public bool IsInRole(string role) => User.IsInRole(role);
}
