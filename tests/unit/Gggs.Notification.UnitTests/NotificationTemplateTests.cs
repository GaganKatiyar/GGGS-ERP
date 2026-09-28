using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Notification.Domain.Notifications;
using Shouldly;

namespace Gggs.Notification.UnitTests;

public sealed class NotificationTemplateTests
{
    [Fact]
    public void Create_ForEmail_RequiresASubject()
    {
        Should.Throw<DomainRuleException>(() =>
            NotificationTemplate.Create("user.invited", NotificationChannel.Email, "en", null, "body"));
    }

    [Fact]
    public void Create_ForInApp_DoesNotRequireASubject()
    {
        NotificationTemplate template = NotificationTemplate.Create(
            "user.invited", NotificationChannel.InApp, "en", null, "body");

        template.Subject.ShouldBeNull();
    }

    [Fact]
    public void Create_NormalizesTheEventCodeAndLocale()
    {
        NotificationTemplate template = NotificationTemplate.Create(
            " User.Invited ", NotificationChannel.InApp, " EN ", null, "body");

        template.EventCode.ShouldBe("user.invited");
        template.Locale.ShouldBe("en");
    }

    [Fact]
    public void Deactivate_ThenActivate_RoundTrips()
    {
        NotificationTemplate template = NotificationTemplate.Create(
            "user.invited", NotificationChannel.InApp, "en", null, "body");

        template.Deactivate();
        template.IsActive.ShouldBeFalse();

        template.Activate();
        template.IsActive.ShouldBeTrue();
    }
}
