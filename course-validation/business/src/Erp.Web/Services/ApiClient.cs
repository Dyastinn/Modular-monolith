using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Erp.Web.Services;

public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

// Every call to the API goes through here, so every page handles errors the same way: the API's problem details
// become a message on screen, and a 401 (session ended) sends the user back to sign in.
public sealed class ApiClient(HttpClient http, ISnackbar snackbar, CookieAuthenticationStateProvider auth, NavigationManager nav)
{
    private sealed record Problem(string? Title, string? Detail, int? Status, string? Code);
    private sealed record Created(long Id);

    public async Task<T?> Get<T>(string url)
    {
        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : await Fail<T>(response);
    }

    // One page of a list for a MudTable: the table counts pages from 0, the API from 1.
    public async Task<TableData<T>> Page<T>(string url, TableState state)
    {
        var separator = url.Contains('?') ? '&' : '?';
        var result = await Get<PagedResult<T>>($"{url}{separator}page={state.Page + 1}&pageSize={state.PageSize}");
        return new TableData<T> { Items = result?.Items ?? [], TotalItems = result?.TotalCount ?? 0 };
    }

    public Task<bool> Post(string url, object? body = null, string? success = null) => Send(HttpMethod.Post, url, body, success);
    public Task<bool> Put(string url, object body, string? success = null) => Send(HttpMethod.Put, url, body, success);
    public Task<bool> Delete(string url, string? success = null) => Send(HttpMethod.Delete, url, null, success);

    // For "create" calls: the API answers 201 with { id }.
    public async Task<long?> Create(string url, object body, string? success = null)
    {
        var response = await http.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode) return await Fail<long?>(response);
        if (success is not null) snackbar.Add(success, Severity.Success);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<bool> Send(HttpMethod method, string url, object? body, string? success)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return await Fail<bool>(response);
        if (success is not null) snackbar.Add(success, Severity.Success);
        return true;
    }

    private async Task<T?> Fail<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            auth.Refresh();   // the session ended; the layout sends them to the sign-in page
            return default;
        }

        var problem = await ReadProblem(response);
        var message = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
            _ => problem?.Detail ?? problem?.Title ?? $"The server answered {(int)response.StatusCode}."
        };
        // The code goes with the message, and its catalog entry is one click away (see Pages/Errors.razor).
        if (problem?.Code is { } code)
            message = $"{message} ({code})";
        snackbar.Add(message, response.StatusCode == HttpStatusCode.Conflict ? Severity.Warning : Severity.Error, options =>
        {
            if (problem?.Code is not { } code) return;
            options.Action = "What does this mean?";
            options.OnClick = _ => { nav.NavigateTo($"errors/{code}"); return Task.CompletedTask; };
        });
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
