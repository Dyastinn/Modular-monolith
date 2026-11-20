using System.Globalization;
using System.Security.Claims;
using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Http;

namespace Yasda.Shared.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null) throw new InvalidOperationException("No HTTP context.");
            return context.User;
        }
    }

    public long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    public string Username => User.FindFirstValue(ClaimTypes.Name)!;
    public string Role => User.FindFirstValue(ClaimTypes.Role)!;   // every account has exactly one role
}
