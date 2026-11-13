using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;


internal sealed record RecordPaymentRequest(decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference,
    decimal TaxWithheld = 0);
internal sealed record VoidRequest(string Reason);

internal sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.TaxWithheld).GreaterThanOrEqualTo(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.Amount + x.TaxWithheld).GreaterThan(0).WithName("Amount").WithMessage("Enter the amount received, the tax withheld, or both.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

// Voiding a payment, an invoice or a withholding certificate always needs a reason.
internal sealed class VoidRequestValidator : AbstractValidator<VoidRequest>
{
    public VoidRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal static class RecordPaymentEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id:long}/payments",
                async Task<Results<Created<CreatedId>, NotFound>> (long id, RecordPaymentRequest body, RecordPaymentHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new RecordPaymentCommand(id, body.Amount, body.TaxWithheld, body.PaidOn, body.Method, body.Reference), ct)
                        is { } paymentId
                        ? TypedResults.Created($"/api/invoices/{id}", new CreatedId(paymentId))
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/invoices/{id:long}/payments/{paymentId:long}/void",
                async Task<Results<NoContent, NotFound>> (long id, long paymentId, VoidRequest body, VoidPaymentHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new VoidPaymentCommand(id, paymentId, body.Reason), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}