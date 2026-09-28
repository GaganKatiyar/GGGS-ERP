using Gggs.Notification.Domain.Notifications;
using Shouldly;

namespace Gggs.Notification.UnitTests;

public sealed class InAppNotificationTests
{
    [Fact]
    public void MarkRead_SetsTheReadDate()
    {
        InAppNotification notification = InAppNotification.Create(
            Guid.CreateVersion7(), "user.invited", "Welcome", "Body", null);
        var now = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

        notification.MarkRead(now);

        notification.IsRead.ShouldBeTrue();
        notification.ReadDate.ShouldBe(now);
    }

    [Fact]
    public void MarkRead_CalledTwice_KeepsTheFirstReadDate()
    {
        InAppNotification notification = InAppNotification.Create(
            Guid.CreateVersion7(), "user.invited", "Welcome", "Body", null);
        var first = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
        var second = first.AddHours(1);

        notification.MarkRead(first);
        notification.MarkRead(second);

        notification.ReadDate.ShouldBe(first);
    }
}
