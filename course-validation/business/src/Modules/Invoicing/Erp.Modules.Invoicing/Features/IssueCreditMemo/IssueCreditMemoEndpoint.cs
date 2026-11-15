using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

// ReturnToStock: the goods came back and can be sold again. Leave it off for a price correction, or goods that are lost.
internal sealed record IssueCreditMemoRequest(IReadOnlyList<CreditLineRequest> Lines, string Reason, bool ReturnToStock);
internal sealed record CreditLineRequest(long LineId, decimal Quantity);

internal sealed class IssueCreditMemoRequestValidator : AbstractValidator<IssueCreditMemoRequest>
{
    public IssueCreditMemoRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
        });
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed record RecordRefundRequest(DateOnly PaidOn, PaymentMethod Method, string? Reference);

internal sealed class RecordRefundRequestValidator : AbstractValidator<RecordRefundRequest>
{
    public RecordRefundRequestValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

internal static class IssueCreditMemoEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id:long}/credit-memos",
                async Task<Results<Created<CreatedId>, NotFound>> (long id, IssueCreditMemoRequest body, IssueCreditMemoHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new IssueCreditMemoCommand(id, body.Lines.Select(l => (l.LineId, l.Quantity)).ToList(),
                        body.Reason, body.ReturnToStock), ct) is { } memoId
                        ? TypedResults.Created($"/api/credit-memos/{memoId}", new CreatedId(memoId))
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/credit-memos/{id:long}/refund",
                async Task<Results<NoContent, NotFound>> (long id, RecordRefundRequest body, RecordRefundHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new RecordRefundCommand(id, body.PaidOn, body.Method, body.Reference), ct)
                        ? TypedResults.NoContent()
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}
