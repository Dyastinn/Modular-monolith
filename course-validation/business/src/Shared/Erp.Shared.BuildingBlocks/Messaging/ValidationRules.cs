using FluentValidation;

namespace Erp.Shared.BuildingBlocks;

// Shape rules that many requests share, written once.
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) => rule.GreaterThan(0);

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) => rule.InclusiveBetween(1, 100);

    // Up to three decimal places; whether fractions are allowed at all is the item's business, not the request's.
    public static IRuleBuilderOptions<T, decimal> ValidQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).Must(q => decimal.Round(q, 3) == q).WithMessage("Quantity allows at most three decimal places.");
}
