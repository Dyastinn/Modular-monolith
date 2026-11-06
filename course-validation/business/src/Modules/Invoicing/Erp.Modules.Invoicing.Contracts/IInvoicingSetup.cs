using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Contracts;

// The company's tax setup, kept by Invoicing on its Company and tax screen, for the modules that need it.
public interface IInvoicingSetup
{
    Task<TaxPolicy> GetTaxPolicyAsync(CancellationToken ct);

    // Throws a DomainException until the company's registered details are filled in.
    Task EnsureReadyToInvoiceAsync(CancellationToken ct);
}
