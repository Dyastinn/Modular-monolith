namespace Erp.Modules.Sales.Domain;

// e.g. SO-2026-000042
internal static class OrderNumbering
{
    public static string Format(long sequence, DateTime nowUtc) => $"SO-{nowUtc:yyyy}-{sequence:000000}";
}
