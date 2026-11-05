using System.Security.Cryptography;
using System.Text;

namespace Erp.Api.Security;

// Identifies the client APP (not the user) on every request, so one app's key can be revoked on its own.
internal sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "X-Api-Key";

    private readonly (string Client, byte[] Key)[] _keys =
        config.GetSection("ApiKeys").Get<Dictionary<string, string>>()
            ?.Where(kv => !string.IsNullOrEmpty(kv.Value))
             .Select(kv => (kv.Key, Encoding.UTF8.GetBytes(kv.Value)))
             .ToArray()
        ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        // Browsers never send custom headers on a CORS preflight, so OPTIONS must pass.
        if (HttpMethods.IsOptions(context.Request.Method) || IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Request.Headers.TryGetValue(HeaderName, out var provided);
        var client = Match(provided!);
        if (client is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;
        }

        context.Items["ClientId"] = client;
        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");

    private string? Match(string provided)
    {
        if (string.IsNullOrEmpty(provided)) return null;
        var bytes = Encoding.UTF8.GetBytes(provided);
        foreach (var (client, key) in _keys)
            if (CryptographicOperations.FixedTimeEquals(bytes, key))   // constant time: no timing leak
                return client;
        return null;
    }
}
