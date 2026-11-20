using FluentValidation;

namespace Yasda.Shared.BuildingBlocks;

// Shape rules that many requests share, written once.
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.InclusiveBetween(1, 100);
    }

    // A list's sort column: one of the names its SQL function knows (the JSON names of its columns), or none.
    // The SQL never builds an ORDER BY from the text, so this list is the whole contract.
    public static IRuleBuilderOptions<T, string?> ValidSort<T>(this IRuleBuilder<T, string?> rule, params string[] columns)
    {
        return rule.Must(sort => sort is null || columns.Contains(sort, StringComparer.Ordinal))
            .WithMessage($"Sort by one of: {string.Join(", ", columns)}.");
    }
}
