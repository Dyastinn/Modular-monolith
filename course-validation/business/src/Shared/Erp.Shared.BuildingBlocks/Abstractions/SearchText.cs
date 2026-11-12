namespace Erp.Shared.BuildingBlocks;

// For the SQL functions' ILIKE '%' || p_search || '%': trimmed, null when blank, and with LIKE's own wildcards escaped,
// so a search for "50%" finds "50%" instead of everything that contains "50".
public static class SearchText
{
    public static string? ForLike(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
