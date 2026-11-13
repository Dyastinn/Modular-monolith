namespace Erp.Api.Security;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        // JSON never needs scripts, styles or frames. The Blazor app needs its own files, WebAssembly (which
        // browsers class as eval) and MudBlazor's inline styles; nothing from any other site. The API browser
        // (/scalar) is development-only and brings its own scripts.
        if (ctx.Request.Path.StartsWithSegments("/api"))
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        else if (!ctx.Request.Path.StartsWithSegments("/scalar"))
            headers.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; " +
                "form-action 'self'; frame-ancestors 'none'";

        return next(ctx);
    }
}
