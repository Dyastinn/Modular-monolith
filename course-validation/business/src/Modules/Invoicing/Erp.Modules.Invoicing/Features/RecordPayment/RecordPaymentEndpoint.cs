using Carter;
using Erp.Modules.Invoicing.Domain;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed record RecordPaymentRequest(decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference);
internal sealed record VoidRequest(string Reason);

internal sealed class RecordPaymentEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id:long}/payments", async (long id, RecordPaymentRequest body, ISender sender, CancellationToken ct) =>
            {
                var paymentId = await sender.Send(new RecordPaymentCommand(id, body.Amount, body.PaidOn, body.Method, body.Reference), ct);
                return Results.Created($"/api/invoices/{id}", new { id = paymentId });
            })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/invoices/{id:long}/payments/{paymentId:long}/void",
                async (long id, long paymentId, VoidRequest body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new VoidPaymentCommand(id, paymentId, body.Reason), ct);
                    return Results.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}
