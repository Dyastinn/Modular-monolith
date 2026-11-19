using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Yasda.Api;

// Tells the API browser that a client other than the web app signs in with a bearer token, so it offers a box for one.
// The web app itself uses the sign-in cookie, which the browser sends to /scalar too.
internal sealed class BearerSecurityScheme : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        if (document.Components is null) document.Components = new OpenApiComponents();
        if (document.Components.SecuritySchemes is null) document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "The accessToken from POST /api/auth/login."
        };
        if (document.Security is null) document.Security = [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
