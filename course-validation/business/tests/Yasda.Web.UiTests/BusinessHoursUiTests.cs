using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class BusinessHoursUiTests : UiTest
{
    [UiFact]
    public async Task An_admin_opens_saturday_for_a_morning_and_closes_it_again()
    {
        var page = await SignIn("admin@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Business hours" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Business hours" })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Time zone")).ToHaveValueAsync("Asia/Manila");
        await Expect(page.GetByLabel("Monday opens at")).ToHaveValueAsync("08:00");

        await page.GetByLabel("Open on Saturday").CheckAsync();
        await page.GetByLabel("Saturday opens at").FillAsync("09:00");
        await page.GetByLabel("Saturday closes at").FillAsync("12:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save working week" }).ClickAsync();
        await Expect(StatusSaying(page, "Working week saved.")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Saturday closes at")).ToHaveValueAsync("12:00");

        await page.GetByLabel("Open on Saturday").UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save working week" }).ClickAsync();
        await Expect(StatusSaying(page, "Working week saved.")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Saturday opens at")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task A_closing_time_before_the_opening_time_is_refused_with_its_code()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/business-hours");

        await page.GetByLabel("Monday closes at").FillAsync("07:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save working week" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("A day must close after it opens");
        await Expect(page.GetByLabel("Monday closes at")).ToHaveValueAsync("17:00");
    }

    [UiFact]
    public async Task An_admin_adds_imports_and_removes_holidays_and_loads_a_countrys_list()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/business-hours");

        await page.GetByLabel("Date").FillAsync("2090-03-04");
        await page.GetByLabel("Name").FillAsync("Founders' Day");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add holiday" }).ClickAsync();
        await Expect(StatusSaying(page, "Holiday added.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Founders' Day", Exact = true })).ToBeVisibleAsync();

        await page.GetByLabel("Holidays, one per line").FillAsync("date,name\n2090-03-05,Day after\nnot a date,Oops");
        await page.GetByRole(AriaRole.Button, new() { Name = "Import holidays" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Line 3");
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Day after", Exact = true })).ToHaveCountAsync(0);

        await page.GetByLabel("Holidays, one per line").FillAsync("date,name\n2090-03-05,Day after");
        await page.GetByRole(AriaRole.Button, new() { Name = "Import holidays" }).ClickAsync();
        await Expect(StatusSaying(page, "1 holidays added, 0 were already there.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Day after", Exact = true })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Load list" }).ClickAsync();
        await Expect(StatusSaying(page, " were already there.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Christmas Day", Exact = true }).First).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Founders' Day" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Remove this holiday?");
        await page.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Founders' Day", Exact = true })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Founders' Day" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes, remove Founders' Day" }).ClickAsync();
        await Expect(StatusSaying(page, "Founders' Day removed.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Founders' Day", Exact = true })).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Day after" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes, remove Day after" }).ClickAsync();
        await Expect(StatusSaying(page, "Day after removed.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Someone_who_is_not_an_admin_neither_sees_nor_opens_the_business_hours_page()
    {
        var page = await SignIn("lead@yasda.local");

        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Business hours" })).ToHaveCountAsync(0);
        await page.GotoAsync("/business-hours");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("You don't have access to this page.");
    }
}
