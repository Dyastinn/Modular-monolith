using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// What the knowledge-base tests share: the shapes the API answers with, and the calls every test makes.
internal static class KnowledgeApi
{
    public sealed record Created(long Id);
    public sealed record Me(long Id, string FullName);
    public sealed record Version(int Number, string Title, string Visibility, string PublishedByName);
    public sealed record Editor(long UserId, string UserName, string AddedByName);
    public sealed record HistoryLine(string Action, string ActorName, DateTime AtUtc, string? Note, int? VersionNumber);
    public sealed record ArticleView(long Id, string Title, string Summary, string Body, string Status, string Visibility, int CurrentVersion,
        string CreatedByName, string LastEditedByName, string? RejectionReason, bool CanEdit, bool CanManageEditors,
        List<Version> Versions, List<Editor> Editors, List<HistoryLine> History);
    public sealed record Row(long Id, string Title, string Status, string Visibility, string CreatedByName);
    public sealed record StaffPage(List<Row> Items, int TotalCount);
    public sealed record PublishedRow(long Id, string Title, string Summary, string Visibility);
    public sealed record PublishedPage(List<PublishedRow> Items, int TotalCount);
    public sealed record PublishedArticle(long Id, string Title, string Summary, string Body, string Visibility, int VersionNumber, DateTime PublishedUtc);
    public sealed record Popular(long Id, string Title, long Views);

    // A title nobody else has, so a test can find its own article among all the others.
    public static string UniqueTitle(string start = "Article")
    {
        return $"{start} {Guid.NewGuid():N}";
    }

    public static async Task<long> CreateAsync(HttpClient author, string? title = null, string visibility = "Public")
    {
        var response = await author.PostAsJsonAsync("/api/articles", new
        {
            title = title ?? UniqueTitle(),
            summary = "A short summary.",
            body = "First paragraph.\n\nSecond paragraph.",
            visibility,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    public static async Task<HttpResponseMessage> EditAsync(HttpClient client, long id, string? title = null, string visibility = "Public")
    {
        return await client.PutAsJsonAsync($"/api/articles/{id}", new
        {
            title = title ?? UniqueTitle("Edited"),
            summary = "Another summary.",
            body = "Edited body.",
            visibility,
        });
    }

    public static async Task<HttpResponseMessage> StepAsync(HttpClient client, long id, string step)
    {
        return await client.PostAsync($"/api/articles/{id}/{step}", content: null);
    }

    public static async Task SubmitAndPublishAsync(HttpClient author, HttpClient editor, long id)
    {
        (await StepAsync(author, id, "submit")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StepAsync(editor, id, "publish")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // A new article, already published as a version readers can see.
    public static async Task<long> CreatePublishedAsync(HttpClient author, HttpClient editor, string? title = null, string visibility = "Public")
    {
        var id = await CreateAsync(author, title, visibility);
        await SubmitAndPublishAsync(author, editor, id);
        return id;
    }

    public static async Task<ArticleView> GetAsync(HttpClient staff, long id)
    {
        var response = await staff.GetAsync($"/api/articles/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ArticleView>())!;
    }

    public static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    public static async Task<long> IdOfAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");
        return me!.Id;
    }
}
