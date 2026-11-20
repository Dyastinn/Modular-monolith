using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Sla.Features;

internal static class GetPoliciesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/policies", async Task<Ok<List<PolicyDto>>> (GetPoliciesHandler handler, CancellationToken ct) =>
        {
            var policies = await handler.HandleAsync(ct);
            return TypedResults.Ok(policies);
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Readers));
    }
}
