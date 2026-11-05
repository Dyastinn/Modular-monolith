namespace Erp.Web.Shared;

public static class Url
{
    // api/sales-orders?status=Approved&search=acme, leaving out anything empty: the API would reject "status=".
    public static string With(string path, params (string Name, object? Value)[] query)
    {
        var parts = query
            .Where(q => q.Value is not null && q.Value.ToString() is { Length: > 0 })
            .Select(q => $"{q.Name}={Uri.EscapeDataString(Convert.ToString(q.Value, System.Globalization.CultureInfo.InvariantCulture)!)}");
        var queryString = string.Join("&", parts);
        return queryString.Length == 0 ? path : $"{path}?{queryString}";
    }
}
