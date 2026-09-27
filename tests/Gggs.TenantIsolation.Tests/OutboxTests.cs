using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Sample.Api.Domain;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.TenantIsolation.Tests;

/// <summary>Stand-in for a real integration event.</summary>
[MessageType("gggs.sample.record.created")]
public sealed record SampleRecordCreatedIntegrationEvent(Guid RecordId, string Code) : IntegrationEvent;

/// <summary>An event that forgot to declare its wire name.</summary>
public sealed record UnnamedIntegrationEvent : IntegrationEvent;

/// <summary>
/// Proves the outbox guarantee against a real database: a message is published if and only if the
/// business change it describes was committed.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class OutboxTests(SqlServerFixture fixture)
{
    private static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GlobexTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SavingTheBusinessChange_AlsoCommitsItsMessage()
    {
        await using SampleDbContext context = fixture.CreateContext(AcmeTenantId);
        IIntegrationEventPublisher publisher = CreatePublisher(context, AcmeTenantId);

        SampleRecord record = SampleRecord.Create("OUTBOX-COMMIT", "Outbox probe");
        context.SampleRecords.Add(record);
        publisher.Publish(new SampleRecordCreatedIntegrationEvent(record.Id, record.Code));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using SampleDbContext verify = fixture.CreateContext(AcmeTenantId);
        OutboxMessage? staged = await verify.Set<OutboxMessage>()
            .SingleOrDefaultAsync(
                message => message.MessageType == "gggs.sample.record.created" && message.TenantId == AcmeTenantId,
                TestContext.Current.CancellationToken);

        staged.ShouldNotBeNull();
        staged.Status.ShouldBe(OutboxStatus.Pending);
        staged.Payload.ShouldContain(record.Id.ToString());
    }

    [Fact]
    public async Task RollingBackTheBusinessChange_DiscardsItsMessage()
    {
        // The guarantee that matters most: no consumer should ever be told about something that
        // did not happen. Publishing inside the handler would already have sent this.
        await using SampleDbContext context = fixture.CreateContext(AcmeTenantId);
        IIntegrationEventPublisher publisher = CreatePublisher(context, AcmeTenantId);

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            SampleRecord record = SampleRecord.Create("OUTBOX-ROLLBACK", "Rollback probe");
            context.SampleRecords.Add(record);
            publisher.Publish(new SampleRecordCreatedIntegrationEvent(record.Id, record.Code));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using SampleDbContext verify = fixture.CreateContext(AcmeTenantId);

        bool recordExists = await verify.SampleRecords
            .IgnoreQueryFilters()
            .AnyAsync(record => record.Code == "OUTBOX-ROLLBACK", TestContext.Current.CancellationToken);

        bool messageExists = await verify.Set<OutboxMessage>()
            .AnyAsync(message => message.Payload.Contains("OUTBOX-ROLLBACK"), TestContext.Current.CancellationToken);

        recordExists.ShouldBeFalse();
        messageExists.ShouldBeFalse();
    }

    [Fact]
    public async Task StagedMessages_CarryTheTenantOfTheOperationThatRaisedThem()
    {
        await using SampleDbContext context = fixture.CreateContext(GlobexTenantId);
        IIntegrationEventPublisher publisher = CreatePublisher(context, GlobexTenantId);

        publisher.Publish(new SampleRecordCreatedIntegrationEvent(Guid.CreateVersion7(), "GLOBEX-EVENT"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using SampleDbContext verify = fixture.CreateContext(GlobexTenantId);
        OutboxMessage staged = await verify.Set<OutboxMessage>()
            .SingleAsync(
                message => message.Payload.Contains("GLOBEX-EVENT"),
                TestContext.Current.CancellationToken);

        // Nothing in the handler named a tenant; it came from the ambient context.
        staged.TenantId.ShouldBe(GlobexTenantId);
    }

    [Fact]
    public async Task TheRelayCanReadTheOutboxAcrossTenants()
    {
        // The relay runs in a platform scope. If the outbox were tenant-filtered or covered by the
        // row-level security policy, this query would come back empty and nothing would ever be
        // published - the failure that made the outbox deliberately not tenant-owned.
        await using (SampleDbContext acme = fixture.CreateContext(AcmeTenantId))
        {
            CreatePublisher(acme, AcmeTenantId)
                .Publish(new SampleRecordCreatedIntegrationEvent(Guid.CreateVersion7(), "RELAY-ACME"));
            await acme.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (SampleDbContext globex = fixture.CreateContext(GlobexTenantId))
        {
            CreatePublisher(globex, GlobexTenantId)
                .Publish(new SampleRecordCreatedIntegrationEvent(Guid.CreateVersion7(), "RELAY-GLOBEX"));
            await globex.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using SampleDbContext relayContext = fixture.CreateContext(tenantId: null);
        List<Guid> tenantIds = await relayContext.Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending)
            .Select(message => message.TenantId)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        tenantIds.ShouldContain(AcmeTenantId);
        tenantIds.ShouldContain(GlobexTenantId);
    }

    [Fact]
    public void AnEventWithoutADeclaredName_IsRejected()
    {
        // The wire name is a published contract; deriving it from the class name would let a
        // rename silently break every consumer.
        Should.Throw<InvalidOperationException>(() => MessageTypeName.For<UnnamedIntegrationEvent>())
            .Message.ShouldContain("MessageType");
    }

    private static IIntegrationEventPublisher CreatePublisher(SampleDbContext context, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        return new OutboxIntegrationEventPublisher(
            context,
            tenantContext,
            new TestCurrentUser(),
            TimeProvider.System);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid UserId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

        public Guid? EmployeeId => null;

        public Guid? SessionId => null;

        public bool IsAuthenticated => true;

        public Guid? ImpersonatorUserId => null;

        public bool IsPlatformStaff => false;

        public string? IpAddress => "127.0.0.1";

        public string? UserAgent => "outbox-tests";

        public string? CorrelationId => "outbox-suite";
    }
}
