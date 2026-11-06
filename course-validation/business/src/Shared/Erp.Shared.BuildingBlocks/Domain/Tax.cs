namespace Erp.Shared.BuildingBlocks;

// How a sale of an item is treated for VAT (BIR RR 7-2024 Sec. 3(B)). Stored as numbers: never renumber.
public enum TaxType
{
    Vatable = 0,
    ZeroRated = 1,
    VatExempt = 2
}
