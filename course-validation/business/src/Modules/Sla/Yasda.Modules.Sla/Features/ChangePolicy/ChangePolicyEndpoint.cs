using Yasda.Modules.Sla.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Sla.Features;

internal sealed record ChangePolicyRequest(int RespondMinutes, int ResolveMinutes);

// Only the shape is checked here (a number, no longer than a year). Whether the numbers make sense together is the policy's
// rule: zero or less, or a resolve target shorter than the response target, answers 422 with SLA-0001 or SLA-0002.
internal sealed class ChangePolicyRequestValidator : AbstractValidator<ChangePolicyRequest>
{
    private const int MinutesInAYear = 525_600;

    public ChangePolicyRequestValidator()
    {
        RuleFor(x => x.RespondMinutes).LessThanOrEqualTo(MinutesInAYear);
        RuleFor(x => x.ResolveMinutes).LessThanOrEqualTo(MinutesInAYear);
    }
}

internal static class ChangePolicyEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // The priority is in the address: a name that is no priority is a 404, like any other thing that doesn't exist.
        app.MapPut("/sla/policies/{priority}",
            async Task<Results<NoContent, NotFound>> (string priority, ChangePolicyRequest body, ChangePolicyHandler handler, CancellationToken ct) =>
        {
            var known = Enum.TryParse<Priority>(priority, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed);
            if (!known) return TypedResults.NotFound();

            var found = await handler.HandleAsync(new ChangePolicyCommand(parsed, body.RespondMinutes, body.ResolveMinutes), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Editors));
    }
}
