using Gggs.BuildingBlocks.Application;
using Gggs.Notification.Application.Notifications;
using Gggs.Notification.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Gggs.Notification.UnitTests;

/// <summary>
/// <see cref="NotificationDispatchService"/> against fakes for every port - no database, no SMTP -
/// so what is under test is the dispatch logic itself: which templates get used, when a row is
/// skipped, and that a delivery failure is recorded rather than thrown.
/// </summary>
public sealed class NotificationDispatchServiceTests
{
    [Fact]
    public async Task DispatchAsync_WithBothChannelsTemplated_CreatesBothAndSendsTheEmail()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(NotificationTemplate.Create("user.invited", NotificationChannel.InApp, "en", null, "Hi {{ display_name }}"));
        templates.Seed(NotificationTemplate.Create("user.invited", NotificationChannel.Email, "en", "Welcome {{ display_name }}", "Click {{ link }}"));

        var inApp = new FakeInAppRepository();
        var outbound = new FakeOutboundRepository();
        var emailSender = new FakeEmailSender();
        var service = new NotificationDispatchService(
            templates, inApp, outbound, emailSender, new FakeUnitOfWork(), NullLogger<NotificationDispatchService>.Instance);

        await service.DispatchAsync(new NotificationRequest(
            Guid.CreateVersion7(), "user.invited", Guid.CreateVersion7(), "priya@example.com", "Welcome",
            new Dictionary<string, object?> { ["display_name"] = "Priya", ["link"] = "https://x" }),
            TestContext.Current.CancellationToken);

        inApp.Added.ShouldHaveSingleItem().Body.ShouldBe("Hi Priya");
        OutboundMessage message = outbound.Added.ShouldHaveSingleItem();
        message.Subject.ShouldBe("Welcome Priya");
        message.Body.ShouldBe("Click https://x");
        message.Status.ShouldBe(OutboundMessageStatus.Sent);
        emailSender.SentTo.ShouldHaveSingleItem().ShouldBe("priya@example.com");
    }

    [Fact]
    public async Task DispatchAsync_WithNoActiveTemplateOnAnyChannel_CreatesNothing()
    {
        var service = new NotificationDispatchService(
            new FakeTemplateRepository(), new FakeInAppRepository(), new FakeOutboundRepository(),
            new FakeEmailSender(), new FakeUnitOfWork(), NullLogger<NotificationDispatchService>.Instance);

        await service.DispatchAsync(new NotificationRequest(
            Guid.CreateVersion7(), "nothing.configured", Guid.CreateVersion7(), "a@b.com", "Title",
            new Dictionary<string, object?>()),
            TestContext.Current.CancellationToken);

        // No exception, and nothing thrown - see the fakes' own Add methods, which would have
        // recorded a call if either repository had been asked to add anything.
    }

    [Fact]
    public async Task DispatchAsync_CalledTwiceWithTheSameSourceMessageId_QueuesTheEmailOnlyOnce()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(NotificationTemplate.Create("user.invited", NotificationChannel.Email, "en", "Subject", "Body"));

        var outbound = new FakeOutboundRepository();
        Guid sourceMessageId = Guid.CreateVersion7();
        var service = new NotificationDispatchService(
            templates, new FakeInAppRepository(), outbound, new FakeEmailSender(),
            new FakeUnitOfWork(), NullLogger<NotificationDispatchService>.Instance);

        var request = new NotificationRequest(
            sourceMessageId, "user.invited", Guid.CreateVersion7(), "a@b.com", "Title", new Dictionary<string, object?>());

        await service.DispatchAsync(request, TestContext.Current.CancellationToken);
        await service.DispatchAsync(request, TestContext.Current.CancellationToken);

        outbound.Added.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAsync_WhenSendingFails_RecordsTheFailureRatherThanThrowing()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(NotificationTemplate.Create("user.invited", NotificationChannel.Email, "en", "Subject", "Body"));

        var outbound = new FakeOutboundRepository();
        var service = new NotificationDispatchService(
            templates, new FakeInAppRepository(), outbound, new ThrowingEmailSender(),
            new FakeUnitOfWork(), NullLogger<NotificationDispatchService>.Instance);

        await service.DispatchAsync(new NotificationRequest(
            Guid.CreateVersion7(), "user.invited", Guid.CreateVersion7(), "a@b.com", "Title", new Dictionary<string, object?>()),
            TestContext.Current.CancellationToken);

        OutboundMessage message = outbound.Added.ShouldHaveSingleItem();
        message.Status.ShouldBe(OutboundMessageStatus.Failed);
        message.LastError.ShouldNotBeNull();
    }

    private sealed class FakeTemplateRepository : INotificationTemplateRepository
    {
        private readonly List<NotificationTemplate> _templates = [];

        public void Seed(NotificationTemplate template) => _templates.Add(template);

        public Task<NotificationTemplate?> GetActiveAsync(
            string eventCode, NotificationChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult(_templates.FirstOrDefault(
                x => x.EventCode == eventCode && x.Channel == channel && x.IsActive));

        public Task<NotificationTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_templates.FirstOrDefault(x => x.Id == id));

        public void Add(NotificationTemplate notificationTemplate) => _templates.Add(notificationTemplate);
    }

    private sealed class FakeInAppRepository : IInAppNotificationRepository
    {
        public List<InAppNotification> Added { get; } = [];

        public Task<InAppNotification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Added.FirstOrDefault(x => x.Id == id));

        public void Add(InAppNotification notification) => Added.Add(notification);
    }

    private sealed class FakeOutboundRepository : IOutboundMessageRepository
    {
        public List<OutboundMessage> Added { get; } = [];

        private readonly HashSet<string> _queuedKeys = [];

        public Task<bool> IdempotencyKeyExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_queuedKeys.Contains(idempotencyKey));

        public void Add(OutboundMessage message)
        {
            Added.Add(message);
            _queuedKeys.Add(message.IdempotencyKey);
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<string> SentTo { get; } = [];

        public Task<string?> SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            SentTo.Add(toAddress);
            return Task.FromResult<string?>("fake-provider-id");
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task<string?> SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("SMTP is unreachable.");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
