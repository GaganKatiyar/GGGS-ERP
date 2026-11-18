using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Platform.Application.Usage;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The HR side of the handshake with the platform console: an organisation created in NaBPlatform is
/// seeded here by the worker's platform-sync job, and the console's administrator requests are carried
/// out inside the organisation - without the platform ever touching this database.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlatformSyncTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task AnOrganisationCreatedInTheConsole_IsSeededHere_AndItsAdministratorRequestsAreCarriedOut()
    {
        string code = $"sync-{Guid.NewGuid():N}"[..20];
        Guid tenantId;
        Guid seedRequest = Guid.CreateVersion7();

        // What the platform console writes: the organisation, its subscription and a seeding request.
        await using (PlatformCatalogDbContext catalog = Catalog())
        {
            Plan plan = await catalog.Plans.FirstAsync(row => row.Code == "PRO", TestContext.Current.CancellationToken);
            Tenant tenant = Tenant.Register(code, $"Customer {code}", "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN", TenantIsolationTier.Pooled, DateTime.UtcNow);
            tenant.CompleteProvisioning(DateTime.UtcNow);
            tenantId = tenant.Id;
            catalog.Tenants.Add(tenant);
            catalog.Subscriptions.Add(Subscription.Start(tenant.Id, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, 50, null));
            catalog.ProvisioningRequests.Add(Request(seedRequest, tenant.Id, ProvisioningRequestKind.SeedOrganisation, $"owner@{code}.example"));
            await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunSyncAsync();
        await factory.DeliverEventsAsync();

        await using (PlatformCatalogDbContext catalog = Catalog())
        {
            (await catalog.ProvisioningRequests.SingleAsync(row => row.Id == seedRequest, TestContext.Current.CancellationToken)).Status.ShouldBe(ProvisioningRequestStatus.Done);
            (await catalog.TenantSeedStates.AnyAsync(row => row.TenantId == tenantId, TestContext.Current.CancellationToken)).ShouldBeTrue();
        }

        // Seeded from the event: the first administrator exists here, invited by email.
        await using (AppDbContext db = factory.CreateContext(tenantId))
        {
            User owner = await db.Set<User>().SingleAsync(user => user.Email == $"owner@{code}.example", TestContext.Current.CancellationToken);
            owner.Status.ShouldBe(UserStatus.Invited);
            owner.InvitationTokenHash.ShouldNotBeNull();
        }

        // The console asks for another administrator; the next pass invites them and reports both.
        Guid addRequest = Guid.CreateVersion7();
        await using (PlatformCatalogDbContext catalog = Catalog())
        {
            catalog.ProvisioningRequests.Add(Request(addRequest, tenantId, ProvisioningRequestKind.AddAdministrator, $"deputy@{code}.example"));
            await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunSyncAsync();

        await using (PlatformCatalogDbContext catalog = Catalog())
        {
            ProvisioningRequestRow done = await catalog.ProvisioningRequests.SingleAsync(row => row.Id == addRequest, TestContext.Current.CancellationToken);
            done.Status.ShouldBe(ProvisioningRequestStatus.Done, done.Result);
            (await catalog.TenantAdministrators.Where(row => row.TenantId == tenantId).Select(row => row.Email).ToListAsync(TestContext.Current.CancellationToken))
                .ShouldBe([$"owner@{code}.example", $"deputy@{code}.example"], ignoreOrder: true);
        }

        // Running again changes nothing: a request is carried out once.
        await RunSyncAsync();
        await using (AppDbContext db = factory.CreateContext(tenantId))
        {
            (await db.Set<User>().CountAsync(user => user.Email == $"deputy@{code}.example", TestContext.Current.CancellationToken)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task AFailedRequest_IsMarkedFailedWithTheReason()
    {
        Guid request = Guid.CreateVersion7();
        await using (PlatformCatalogDbContext catalog = Catalog())
        {
            catalog.ProvisioningRequests.Add(Request(request, PeopleOsApiFactory.SeededTenantId, ProvisioningRequestKind.ResendAdministratorInvitation, "nobody@testco.example"));
            await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunSyncAsync();

        await using PlatformCatalogDbContext check = Catalog();
        ProvisioningRequestRow row = await check.ProvisioningRequests.SingleAsync(r => r.Id == request, TestContext.Current.CancellationToken);
        row.Status.ShouldBe(ProvisioningRequestStatus.Failed);
        row.Result.ShouldNotBeNullOrWhiteSpace();
    }

    private PlatformCatalogDbContext Catalog() => new(
        new DbContextOptionsBuilder<PlatformCatalogDbContext>().UseSqlServer(factory.PlatformConnectionString).Options);

    private Task RunSyncAsync() => factory.RunAsPlatformJobAsync(services =>
        ActivatorUtilities.CreateInstance<PlatformSyncJob>(services, new NoJobs()).RunAsync());

    private static ProvisioningRequestRow Request(Guid id, Guid tenantId, ProvisioningRequestKind kind, string email) => new()
    {
        Id = id,
        TenantId = tenantId,
        Kind = kind,
        Email = email,
        Status = ProvisioningRequestStatus.Pending,
        RequestedDate = DateTime.UtcNow,
        RequestedBy = "Integration test",
    };

    /// <summary>The tests' worker has no job server; nothing here retries jobs.</summary>
    private sealed class NoJobs : IJobMonitor
    {
        public IReadOnlyList<JobServerInfo> Servers() => [];

        public IReadOnlyList<RecurringJobInfo> RecurringJobs() => [];

        public IReadOnlyList<FailedJobInfo> FailedJobs(int count = 50) => [];

        public JobCounts Counts() => new(0, 0, 0, 0, 0);

        public bool Retry(string jobId) => false;
    }
}
