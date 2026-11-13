using FluentValidation;
using FluentValidation.Results;

namespace Erp.Api;

// Validates every endpoint argument that has a FluentValidation validator, before the endpoint runs. Attached once to
// the /api group, so no endpoint can forget it. A malformed request is answered 400 with its field errors; nothing is
// thrown, because a bad request is an everyday event, not an exceptional one.
internal static class RequestValidation
{
    public static RouteGroupBuilder ValidateRequests(this RouteGroupBuilder group) =>
        group.AddEndpointFilterFactory((context, next) =>
        {
            // Worked out once per endpoint at startup: which arguments have a validator at all.
            var isService = context.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
            var validated = context.MethodInfo.GetParameters()
                .Select((p, index) => (Index: index, Validator: typeof(IValidator<>).MakeGenericType(p.ParameterType)))
                .Where(p => isService.IsService(p.Validator))
                .ToArray();
            if (validated.Length == 0) return next;

            return async invocation =>
            {
                var failures = new List<ValidationFailure>();
                foreach (var (index, validatorType) in validated)
                {
                    if (invocation.Arguments[index] is not { } argument) continue;
                    var validator = (IValidator)invocation.HttpContext.RequestServices.GetRequiredService(validatorType);
                    var result = await validator.ValidateAsync(new ValidationContext<object>(argument), invocation.HttpContext.RequestAborted);
                    failures.AddRange(result.Errors);
                }

                if (failures.Count == 0) return await next(invocation);
                return TypedResults.ValidationProblem(
                    failures.GroupBy(f => f.PropertyName).ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray()),
                    detail: string.Join(" ", failures.Select(f => f.ErrorMessage)));
            };
        });
}
