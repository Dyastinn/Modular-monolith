using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Yasda.Web.Services;

public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

// Every call to the API goes through here, so every page handles errors the same way: the API's problem details
// become the status line's message, and a 401 (session ended) sends the user back to sign in.
public sealed class ApiClient(HttpClient http, Notices notices, CookieAuthenticationStateProvider auth)
{
    private sealed record Problem(string? Title, string? Detail, int? Status, string? Code);
    private sealed record Created(long Id);
    private int _saving;

    // True while a change is on its way to the server. Pages disable their action buttons meanwhile, so a double
    // click can't send the same change twice.
    public bool Busy => _saving > 0;

    public async Task<T?> Get<T>(string url)
    {
        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return await Fail<T>(response);
        return await response.Content.ReadFromJsonAsync<T>();
    }

    // For actions: true when the API said yes. The body is optional ("take this ticket" has none).
    public async Task<bool> Post(string url, object? body = null, string? success = null)
    {
        return await Send(HttpMethod.Post, url, body, success);
    }

    // For changes to something that already exists, such as a ticket's priority.
    public async Task<bool> Put(string url, object body, string? success = null)
    {
        return await Send(HttpMethod.Put, url, body, success);
    }

    public async Task<bool> Delete(string url, string? success = null)
    {
        return await Send(HttpMethod.Delete, url, null, success);
    }

    // For "create" calls: the API answers 201 with { id }.
    public async Task<long?> Create(string url, object body, string? success = null)
    {
        var response = await Change(() => http.PostAsJsonAsync(url, body));
        if (!response.IsSuccessStatusCode) return await Fail<long?>(response);
        if (success is not null) notices.Success(success);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<bool> Send(HttpMethod method, string url, object? body, string? success)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);

        var response = await Change(() => http.SendAsync(request));
        if (!response.IsSuccessStatusCode) return await Fail<bool>(response);
        if (success is not null) notices.Success(success);
        return true;
    }

    // A new change replaces the last message: an old error must not sit next to a change that worked.
    private async Task<HttpResponseMessage> Change(Func<Task<HttpResponseMessage>> send)
    {
        notices.Clear();
        _saving++;
        try
        {
            return await send();
        }
        finally
        {
            _saving--;
        }
    }

    private async Task<T?> Fail<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            auth.Refresh();   // the session ended; the router sends them to the sign-in page
            return default;
        }

        var problem = await ReadProblem(response);

        // The server's own words, when it sent any.
        var serverMessage = $"The server answered {(int)response.StatusCode}.";
        string? code = null;
        if (problem is not null)
        {
            if (problem.Detail is not null) serverMessage = problem.Detail;
            else if (problem.Title is not null) serverMessage = problem.Title;
            code = problem.Code;
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
            _ => serverMessage
        };
        // The status line links the code to its catalog entry (see Pages/Errors.razor).
        notices.Error(message, code);
        return default;
    }

    private static async Task<Problem?> ReadProblem(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>();
        }
        catch (JsonException)
        {
            return null;   // not a problem body (a proxy's error page, say)
        }
    }
}
