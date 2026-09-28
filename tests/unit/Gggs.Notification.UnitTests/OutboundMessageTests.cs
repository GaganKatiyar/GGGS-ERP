using Gggs.Notification.Domain.Notifications;
using Shouldly;

namespace Gggs.Notification.UnitTests;

public sealed class OutboundMessageTests
{
    [Fact]
    public void Create_StartsQueued()
    {
        OutboundMessage message = OutboundMessage.Create(
            Guid.CreateVersion7(), "a@b.com", "user.invited", "Subject", "Body", "key-1");

        message.Status.ShouldBe(OutboundMessageStatus.Queued);
        message.Attempts.ShouldBe(0);
    }

    [Fact]
    public void MarkSent_RecordsTheProviderIdAndClearsAnyPriorError()
    {
        OutboundMessage message = OutboundMessage.Create(
            Guid.CreateVersion7(), "a@b.com", "user.invited", "Subject", "Body", "key-1");
        message.MarkFailed("first attempt failed");

        message.MarkSent("provider-123");

        message.Status.ShouldBe(OutboundMessageStatus.Sent);
        message.ProviderMessageId.ShouldBe("provider-123");
        message.LastError.ShouldBeNull();
        message.Attempts.ShouldBe(2);
    }

    [Fact]
    public void MarkFailed_TruncatesAVeryLongError()
    {
        OutboundMessage message = OutboundMessage.Create(
            Guid.CreateVersion7(), "a@b.com", "user.invited", "Subject", "Body", "key-1");

        message.MarkFailed(new string('x', 3000));

        message.LastError!.Length.ShouldBe(2000);
        message.Status.ShouldBe(OutboundMessageStatus.Failed);
    }
}
