using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;

namespace Yasda.Integration.Tests;

// A category may have its own SLA for a priority. The tests make their own category (all tests share one database), and remove the
// category's policy again at the end.
[Collection("Integration")]
public class SlaCategoryPolicyTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Lead = "lead@yasda.local";

    private sealed record Created(long Id);
    private sealed record Deadlines(DateTime ResponseDueUtc, DateTime ResolveDueUtc);
    private sealed record TicketView(DateTime CreatedUtc, Deadlines? Sla);
    private sealed record CategoryPolicy(long CategoryId, string Priority, int RespondMinutes, int ResolveMinutes, string UpdatedByName);

    // Where a deadline lands under the installation's calendar (Monday to Friday, 08:00 to 17:00, Manila): see SlaTests.
    private static readonly BusinessCalendar OfficeHours = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"),
        new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Tuesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Wednesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Thursday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Friday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        },
        []);

    private static DateTime DueAfter(DateTime createdUtc, TimeSpan openTime)
    {
        var whole = new DateTime(createdUtc.Ticks - createdUtc.Ticks % 10, DateTimeKind.Utc);
        return OfficeHours.Add(whole, openTime);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private async Task<long> NewCategory()
    {
        var lead = await factory.ClientFor(Lead);
        var response = await lead.PostAsJsonAsync("/api/categories", new { name = $"Category {Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<long> FileTicket(long? categoryId, string priority = "High")
    {
        var requester = await factory.ClientFor(Requester);
        var response = await requester.PostAsJsonAsync("/api/tickets",
            new { subject = $"Category SLA {Guid.NewGuid():N}", message = "Please help.", priority, categoryId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<TicketView> ReadUntil(long id, Func<TicketView, bool> arrived)
    {
        var agent = await factory.ClientFor(Agent);
        return (await Waiting.Eventually(() => agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, t => t is not null && arrived(t)))!;
    }

    private async Task SetOwnPolicy(long categoryId, int respondMinutes, int resolveMinutes)
    {
        var lead = await factory.ClientFor(Lead);
        (await lead.PutAsJsonAsync($"/api/sla/category-policies/{categoryId}/High", new { respondMinutes, resolveMinutes })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_ticket_of_a_category_with_its_own_policy_uses_those_numbers_and_other_tickets_do_not()
    {
        var lead = await factory.ClientFor(Lead);
        var category = await NewCategory();
        await SetOwnPolicy(category, 60, 120);
        try
        {
            var inCategory = await ReadUntil(await FileTicket(category), t => t.Sla is not null);
            var elsewhere = await ReadUntil(await FileTicket(null), t => t.Sla is not null);

            inCategory.Sla!.ResponseDueUtc.Should().Be(DueAfter(inCategory.CreatedUtc, TimeSpan.FromMinutes(60)));
            inCategory.Sla.ResolveDueUtc.Should().Be(DueAfter(inCategory.CreatedUtc, TimeSpan.FromMinutes(120)));
            elsewhere.Sla!.ResponseDueUtc.Should().Be(DueAfter(elsewhere.CreatedUtc, TimeSpan.FromHours(4)), "High is 4 hours for everyone else");
        }
        finally
        {
            await lead.DeleteAsync($"/api/sla/category-policies/{category}/High");
        }
    }

    [Fact]
    public async Task Moving_a_ticket_to_another_category_changes_its_deadlines_and_removing_the_policy_changes_them_back()
    {
        var lead = await factory.ClientFor(Lead);
        var category = await NewCategory();
        var ticketId = await FileTicket(null);
        var original = await ReadUntil(ticketId, t => t.Sla is not null);
        await SetOwnPolicy(category, 60, 120);
        try
        {
            (await lead.PostAsJsonAsync($"/api/tickets/{ticketId}/route", new { categoryId = category })).EnsureSuccessStatusCode();
            var moved = await ReadUntil(ticketId, t => t.Sla!.ResponseDueUtc != original.Sla!.ResponseDueUtc);
            moved.Sla!.ResponseDueUtc.Should().Be(DueAfter(moved.CreatedUtc, TimeSpan.FromMinutes(60)));

            (await lead.DeleteAsync($"/api/sla/category-policies/{category}/High")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var back = await ReadUntil(ticketId, t => t.Sla!.ResponseDueUtc == original.Sla!.ResponseDueUtc);
            back.Sla!.ResolveDueUtc.Should().Be(original.Sla!.ResolveDueUtc);
        }
        finally
        {
            await lead.DeleteAsync($"/api/sla/category-policies/{category}/High");
        }
    }

    [Fact]
    public async Task Changing_a_category_policy_moves_the_tickets_already_running_in_it()
    {
        var lead = await factory.ClientFor(Lead);
        var category = await NewCategory();
        var ticketId = await FileTicket(category);
        var before = await ReadUntil(ticketId, t => t.Sla is not null);
        try
        {
            await SetOwnPolicy(category, 30, 90);

            var after = await ReadUntil(ticketId, t => t.Sla!.ResponseDueUtc != before.Sla!.ResponseDueUtc);

            after.Sla!.ResponseDueUtc.Should().Be(DueAfter(after.CreatedUtc, TimeSpan.FromMinutes(30)));
        }
        finally
        {
            await lead.DeleteAsync($"/api/sla/category-policies/{category}/High");
        }
    }

    [Fact]
    public async Task The_category_policies_are_listed_for_staff_and_only_team_leads_and_admins_change_them()
    {
        var lead = await factory.ClientFor(Lead);
        var agent = await factory.ClientFor(Agent);
        var requester = await factory.ClientFor(Requester);
        var category = await NewCategory();
        await SetOwnPolicy(category, 60, 120);
        try
        {
            var listed = await agent.GetFromJsonAsync<List<CategoryPolicy>>("/api/sla/category-policies");

            listed!.Should().ContainSingle(p => p.CategoryId == category).Which.Should().Match<CategoryPolicy>(p => p.Priority == "High" && p.RespondMinutes == 60 && p.ResolveMinutes == 120);
            (await requester.GetAsync("/api/sla/category-policies")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await agent.PutAsJsonAsync($"/api/sla/category-policies/{category}/High", new { respondMinutes = 5, resolveMinutes = 6 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await agent.DeleteAsync($"/api/sla/category-policies/{category}/High")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await lead.DeleteAsync($"/api/sla/category-policies/{category}/High");
        }
    }

    [Fact]
    public async Task A_category_policy_that_makes_no_sense_or_names_nothing_that_exists_is_refused()
    {
        var lead = await factory.ClientFor(Lead);
        var category = await NewCategory();

        var zero = await lead.PutAsJsonAsync($"/api/sla/category-policies/{category}/High", new { respondMinutes = 0, resolveMinutes = 10 });
        var backwards = await lead.PutAsJsonAsync($"/api/sla/category-policies/{category}/High", new { respondMinutes = 10, resolveMinutes = 5 });
        var tooLong = await lead.PutAsJsonAsync($"/api/sla/category-policies/{category}/High", new { respondMinutes = 600_000, resolveMinutes = 700_000 });
        var noCategory = await lead.PutAsJsonAsync("/api/sla/category-policies/999999999/High", new { respondMinutes = 10, resolveMinutes = 20 });
        var noPriority = await lead.PutAsJsonAsync($"/api/sla/category-policies/{category}/Urgent", new { respondMinutes = 10, resolveMinutes = 20 });

        (await CodeOf(zero)).Should().Be("SLA-0001");
        (await CodeOf(backwards)).Should().Be("SLA-0002");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noCategory.StatusCode.Should().Be(HttpStatusCode.NotFound);
        noPriority.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.GetFromJsonAsync<List<CategoryPolicy>>("/api/sla/category-policies"))!.Should().NotContain(p => p.CategoryId == category);
    }
}
