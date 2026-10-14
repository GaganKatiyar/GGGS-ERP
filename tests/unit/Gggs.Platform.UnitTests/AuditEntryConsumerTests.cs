using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Platform.Application.Audit;
using Gggs.Modules.Platform.Domain.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>
/// <see cref="AuditEntryConsumer"/> turns one <see cref="AuditEntryIntegrationEvent"/> into one
/// <see cref="AuditLog"/> row. The tenant and inbox dedup that a real delivery goes through are
/// <see cref="IntegrationEventSubscriber{TDbContext}"/>'s job, not this class's, so those are
/// stubbed here and covered instead by the endpoint tests that read real rows back out.
/// </summary>
public sealed class AuditEntryConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime OccurredDate = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandlingANewEvent_RecordsOneAuditLogCarryingEveryField()
    {
        var repository = new FakeAuditLogRepository();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);
        var consumer = new AuditEntryConsumer(repository, tenantContext, NullLogger<AuditEntryConsumer>.Instance);

        var integrationEvent = new AuditEntryIntegrationEvent(
            "corehr", "Modified", "LegalEntity", "le-1",
            "{\"Name\":\"Before\"}", "{\"Name\":\"After\"}", "Name",
            ActorUserId, "correlation-1")
        {
            OccurredDate = OccurredDate,
        };

        await consumer.HandleAsync(integrationEvent, TestContext.Current.CancellationToken);

        AuditLog recorded = repository.Added.ShouldHaveSingleItem();
        recorded.Id.ShouldBe(integrationEvent.MessageId);
        recorded.TenantId.ShouldBe(TenantId);
        recorded.OccurredDate.ShouldBe(OccurredDate);
        recorded.UserId.ShouldBe(ActorUserId);
        recorded.Service.ShouldBe("corehr");
        recorded.Action.ShouldBe("Modified");
        recorded.EntityType.ShouldBe("LegalEntity");
        recorded.EntityId.ShouldBe("le-1");
        recorded.OldValuesJson.ShouldBe("{\"Name\":\"Before\"}");
        recorded.NewValuesJson.ShouldBe("{\"Name\":\"After\"}");
        recorded.ChangedFieldsCsv.ShouldBe("Name");
        recorded.CorrelationId.ShouldBe("correlation-1");
    }

    [Fact]
    public async Task HandlingAMessageAlreadyRecorded_AddsNothing()
    {
        var repository = new FakeAuditLogRepository { AlreadyExists = true };
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);
        var consumer = new AuditEntryConsumer(repository, tenantContext, NullLogger<AuditEntryConsumer>.Instance);

        var integrationEvent = new AuditEntryIntegrationEvent(
            "corehr", "Created", "LegalEntity", "le-1", null, "{}", null, ActorUserId, null)
        {
            OccurredDate = OccurredDate,
        };

        await consumer.HandleAsync(integrationEvent, TestContext.Current.CancellationToken);

        // A redelivery that somehow reached the handler despite the subscriber's own inbox check
        // must still not duplicate the row - see AuditLogRepository.ExistsAsync's own remarks.
        repository.Added.ShouldBeEmpty();
    }

    /// <summary>An in-memory stand-in for the repository, exercising only what the consumer calls.</summary>
    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public bool AlreadyExists { get; init; }

        public List<AuditLog> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid auditLogId, CancellationToken cancellationToken = default)
            => Task.FromResult(AlreadyExists);

        public void Add(AuditLog auditLog) => Added.Add(auditLog);
    }
}
