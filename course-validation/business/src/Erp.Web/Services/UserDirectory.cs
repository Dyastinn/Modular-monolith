namespace Erp.Web.Services;

// Turns the user ids the API returns ("approved by 2") into names, loaded once per session.
public sealed class UserDirectory(ApiClient api)
{
    private sealed record Entry(long Id, string FullName);
    private Dictionary<long, string>? _names;

    public async Task<string> NameOf(long? id)
    {
        if (id is null) return "";
        _names ??= (await api.Get<List<Entry>>("api/users/directory") ?? []).ToDictionary(e => e.Id, e => e.FullName);
        return _names.TryGetValue(id.Value, out var name) ? name : $"User {id}";
    }
}
