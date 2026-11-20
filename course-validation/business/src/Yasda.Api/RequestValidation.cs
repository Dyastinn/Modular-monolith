using FluentValidation;
using FluentValidation.Results;

namespace Yasda.Api;

// Validates every endpoint argument that has a FluentValidation validator, before the endpoint runs. Attached once to
// the /api group, so no endpoint can forget it. A malformed request is answered 400 with its field errors; nothing is
// thrown, because a bad request is an everyday event, not an exceptional one.
internal static class RequestValidation
{
    public static RouteGroupBuilder ValidateRequests(this RouteGroupBuilder group) =>
        group.AddEndpointFilterFactory((context, next) =>
        {
            // Worked out once per endpoint at startup: which arguments have a validator at all.
            // An argument's type is known only at startup, so its validator type is built by reflection.
            var isService = context.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
            var validated = new List<(int Index, Type Validator)>();
            var parameters = context.MethodInfo.GetParameters();
            for (var index = 0; index < parameters.Length; index++)
            {
                var validatorType = typeof(IValidator<>).MakeGenericType(parameters[index].ParameterType);
                if (isService.IsService(validatorType))
                    validated.Add((index, validatorType));
            }
            if (validated.Count == 0) return next;

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

                // Field name → its messages, the shape a validation problem body expects.
                var messagesByField = new Dictionary<string, List<string>>();
                foreach (var failure in failures)
                {
                    if (!messagesByField.ContainsKey(failure.PropertyName))
                        messagesByField[failure.PropertyName] = new List<string>();
                    messagesByField[failure.PropertyName].Add(failure.ErrorMessage);
                }

                var errors = new Dictionary<string, string[]>();
                foreach (var field in messagesByField)
                    errors[field.Key] = field.Value.ToArray();

                return TypedResults.ValidationProblem(errors, detail: string.Join(" ", failures.Select(f => f.ErrorMessage)));
            };
        });
}
