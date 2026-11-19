namespace Yasda.Web.Shared;

public static class Url
{
    // api/tickets?status=Open&search=printer, leaving out anything empty: the API would reject "status=".
    public static string With(string path, params (string Name, object? Value)[] query)
    {
        var parts = new List<string>();
        foreach (var (name, value) in query)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text)) continue;
            parts.Add($"{name}={Uri.EscapeDataString(text)}");
        }
        if (parts.Count == 0) return path;
        return $"{path}?{string.Join("&", parts)}";
    }
}
