using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Platform.Endpoints;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary><c>/api/v1/help</c>: an organisation's own notes on help pages, and help feedback.</summary>
[Collection(nameof(ApiCollection))]
public sealed class HelpEndpointTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task Notes_WithoutCredentials_AreRefused()
    {
        using HttpClient client = factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/help/notes", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAdministratorsNote_IsReadByEveryone_AndAnEmptiedNoteDisappears()
    {
        using HttpClient admin = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Admin.SettingManage);
        using HttpClient employee = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Self.ProfileView);

        HttpResponseMessage saved = await admin.PutAsJsonAsync(
            "/api/v1/help/notes/me.leave",
            new SaveHelpNoteBody("  Our leave year starts in April.  "),
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        IReadOnlyList<HelpNoteItem>? notes = await employee.GetFromJsonAsync<IReadOnlyList<HelpNoteItem>>("/api/v1/help/notes", TestContext.Current.CancellationToken);
        notes.ShouldNotBeNull();
        notes.ShouldContain(new HelpNoteItem("me.leave", "Our leave year starts in April."));

        (await admin.PutAsJsonAsync("/api/v1/help/notes/me.leave", new SaveHelpNoteBody(""), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        notes = await employee.GetFromJsonAsync<IReadOnlyList<HelpNoteItem>>("/api/v1/help/notes", TestContext.Current.CancellationToken);
        notes.ShouldNotBeNull();
        notes.ShouldNotContain(note => note.TopicId == "me.leave");
    }

    [Fact]
    public async Task OnlySettingsManagers_WriteNotes()
    {
        using HttpClient employee = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Self.ProfileView);

        HttpResponseMessage response = await employee.PutAsJsonAsync(
            "/api/v1/help/notes/me.leave",
            new SaveHelpNoteBody("Not mine to write."),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ANoteOnSomethingThatIsNotAHelpPage_IsRejected()
    {
        using HttpClient admin = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Admin.SettingManage);

        HttpResponseMessage response = await admin.PutAsJsonAsync(
            "/api/v1/help/notes/Not%20A%20Page",
            new SaveHelpNoteBody("Hello"),
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Feedback_IsAcceptedFromAnyoneSignedIn()
    {
        using HttpClient employee = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Self.ProfileView);

        HttpResponseMessage response = await employee.PostAsJsonAsync(
            "/api/v1/help/feedback",
            new HelpFeedbackBody(HelpFeedbackKind.Unanswered, null, "how do I book a meeting room", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
