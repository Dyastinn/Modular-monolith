using System.Security.Cryptography;
using System.Text;

namespace Erp.Api.Security;

// App-level gate (Phase 4 §9b): every request must carry a valid X-Api-Key identifying a known CLIENT APP,
// checked BEFORE authentication. This is orthogonal to the JWT, which identifies the USER, not the app —
// and it's the one gate that can sit in front of the anonymous /auth/login door.
//
// Multiple named keys (one per client — "mobile", "desktop", …) let us tell clients apart: we can log or
// rate-limit per client, and revoke ONE client's key without disturbing the others.
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "X-Api-Key";

    // Read the configured "ApiKeys" map (client name → key) once at startup, pre-encoded for comparison.
    private readonly (string Client, byte[] Key)[] _keys =
        config.GetSection("ApiKeys").Get<Dictionary<string, string>>()
            ?.Where(kv => !string.IsNullOrEmpty(kv.Value))
             .Select(kv => (kv.Key, Encoding.UTF8.GetBytes(kv.Value)))
             .ToArray()
        ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        // Let CORS preflight through: a browser NEVER sends custom headers (X-Api-Key) on an OPTIONS
        // preflight, so gating it here would 401 the preflight and the real request would never be sent.
        // Also wave through Swagger UI and the health check so the app stays browsable in dev.
        if (HttpMethods.IsOptions(context.Request.Method) || IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Request.Headers.TryGetValue(HeaderName, out var provided);
        var client = Match(provided!);

        if (client is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;   // unknown client — stop here
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;   // short-circuit: never call next() → no endpoint runs
        }

        // Remember WHICH client this is, so downstream (logging, rate limiting) can tell them apart.
        context.Items["ClientId"] = client;
        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");

    // Return the client name whose key matches, or null. Same fixed-time comparison as the §3 password
    // check: FixedTimeEquals returns false on a length mismatch (no throw), so timing can't leak a key.
    private string? Match(string provided)
    {
        if (string.IsNullOrEmpty(provided)) return null;
        var bytes = Encoding.UTF8.GetBytes(provided);
        foreach (var (client, key) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(bytes, key))
                return client;
        }
        return null;
    }
}
