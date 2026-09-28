using Gggs.Notification.Application.Notifications;
using Shouldly;

namespace Gggs.Notification.UnitTests;

public sealed class NotificationTemplateRendererTests
{
    [Fact]
    public void Render_SubstitutesEveryKnownPlaceholder()
    {
        string result = NotificationTemplateRenderer.Render(
            "Hi {{ display_name }}, click {{ link }} to get started.",
            new Dictionary<string, object?> { ["display_name"] = "Priya", ["link"] = "https://app/x" });

        result.ShouldBe("Hi Priya, click https://app/x to get started.");
    }

    [Fact]
    public void Render_LeavesAnUnknownPlaceholderUntouched()
    {
        string result = NotificationTemplateRenderer.Render("Hello {{ missing }}!", new Dictionary<string, object?>());

        result.ShouldBe("Hello {{ missing }}!");
    }

    [Fact]
    public void Render_FormatsADateWithoutATimeComponent()
    {
        string result = NotificationTemplateRenderer.Render(
            "Expires {{ expires_date }}.",
            new Dictionary<string, object?> { ["expires_date"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });

        result.ShouldBe("Expires 1 October 2026.");
    }

    [Fact]
    public void Render_TreatsANullValueAsAnEmptyString()
    {
        string result = NotificationTemplateRenderer.Render(
            "Note: {{ note }}", new Dictionary<string, object?> { ["note"] = null });

        result.ShouldBe("Note: ");
    }
}
