namespace Yasda.Web.Shared;

// The last address of each list in this browser tab, for the trail on a detail page: "Users" goes back to the list
// as you left it (its search, sort and page), not to an empty one.
public sealed class ListMemory
{
    private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string relativeUri) => _last[relativeUri.Split('?')[0]] = relativeUri;

    public string Last(string path) => _last.GetValueOrDefault(path, path);
}
