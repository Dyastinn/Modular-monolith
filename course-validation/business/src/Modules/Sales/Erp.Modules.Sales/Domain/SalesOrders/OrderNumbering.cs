namespace Erp.Modules.Sales.Domain;

// SO-2026-000042. The sequence number comes from a database SEQUENCE (no two callers ever get the same one);
// how it's shown to people is a business convention, so it lives here.
internal static class OrderNumbering
{
    public static string Format(long sequence, DateTime nowUtc) => $"SO-{nowUtc:yyyy}-{sequence:000000}";
}
