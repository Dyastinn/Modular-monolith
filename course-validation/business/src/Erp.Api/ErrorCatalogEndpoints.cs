using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api;

// The error catalog every module contributes to. Public on purpose: the "type" link in an error must open even for
// someone whose session just ended.
internal static class ErrorCatalogEndpoints
{
    public static IEndpointRouteBuilder MapErrorCatalog(this IEndpointRouteBuilder api)
    {
        var errors = api.MapGroup("/errors").AllowAnonymous().WithTags("Errors");

        errors.MapGet("/", (IEnumerable<ErrorCatalog> catalogs) =>
            TypedResults.Ok(catalogs.SelectMany(c => c.Codes).OrderBy(c => c.Code, StringComparer.Ordinal).ToList()));

        errors.MapGet("/{code}", Results<Ok<ErrorCode>, NotFound> (string code, IEnumerable<ErrorCatalog> catalogs) =>
            catalogs.SelectMany(c => c.Codes).FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } error
                ? TypedResults.Ok(error)
                : TypedResults.NotFound());

        return api;
    }
}
