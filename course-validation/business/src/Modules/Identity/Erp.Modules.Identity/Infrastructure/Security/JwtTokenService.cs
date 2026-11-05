using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Modules.Identity.Infrastructure;

// Mints the short-lived access token. Validation of incoming tokens is configured in Erp.Api's Program.cs.
internal sealed class JwtTokenService(IConfiguration config, IClock clock)
{
    public string CreateAccessToken(long userId, string username, string role)
    {
        var jwt = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),   // subject = user id
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),                            // powers RequireRole(...)
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: clock.UtcNow.AddMinutes(int.Parse(jwt["AccessTokenMinutes"]!)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
