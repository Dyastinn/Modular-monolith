using Erp.Modules.Invoicing.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Infrastructure;

// Invoicing's service window for the company's tax setup.
internal sealed class InvoicingSetupService(InvoicingDbContext db) : IInvoicingSetup
{
    public async Task<TaxPolicy> GetTaxPolicyAsync(CancellationToken ct) => (await db.CompanyProfileAsync(ct)).Tax;

    public async Task EnsureReadyToInvoiceAsync(CancellationToken ct) => (await db.CompanyProfileAsync(ct)).EnsureReadyToInvoice();
}
