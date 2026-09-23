using Dapper;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Sample.Api.Domain;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.TenantIsolation.Tests;

/// <summary>
/// The suite Phase 0 exists to produce: proof that a tenant cannot reach another tenant's rows,
/// exercised against a real SQL Server with the production filters, interceptors and security
/// policy in place.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TenantIsolationTests(SqlServerFixture fixture)
{
    private static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GlobexTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task EfQueries_ReturnOnlyTheCurrentTenantsRows()
    {
        Guid acmeRecordId = await SeedAsync(AcmeTenantId, "ACME-READ", "Acme payroll policy");
        await SeedAsync(GlobexTenantId, "GLOBEX-READ", "Globex payroll policy");

        await using SampleDbContext globexContext = fixture.CreateContext(GlobexTenantId);
        List<SampleRecord> visible = await globexContext.SampleRecords.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        visible.ShouldNotBeEmpty();
        visible.ShouldAllBe(record => record.TenantId == GlobexTenantId);
        visible.Any(record => record.Id == acmeRecordId).ShouldBeFalse();
    }

    [Fact]
    public async Task FetchingAnotherTenantsRecordById_FindsNothing()
    {
        Guid acmeRecordId = await SeedAsync(AcmeTenantId, "ACME-BYID", "Only Acme may see this");

        await using SampleDbContext globexContext = fixture.CreateContext(GlobexTenantId);
        SampleRecord? stolen = await globexContext.SampleRecords
            .FirstOrDefaultAsync(record => record.Id == acmeRecordId, cancellationToken: TestContext.Current.CancellationToken);

        // Knowing the exact id is not enough, which is why the API can safely answer 404.
        stolen.ShouldBeNull();
    }

    [Fact]
    public async Task TwoContextsOnDifferentTenants_DoNotShareTheCachedFilterValue()
    {
        // EF caches one compiled model per context type. If the tenant were baked into that cached
        // filter instead of being read per query, the second tenant would see the first tenant's
        // rows - the single most dangerous failure mode in a pooled database.
        await SeedAsync(AcmeTenantId, "ACME-CACHE", "Acme cache probe");
        await SeedAsync(GlobexTenantId, "GLOBEX-CACHE", "Globex cache probe");

        await using SampleDbContext acmeContext = fixture.CreateContext(AcmeTenantId);
        await using SampleDbContext globexContext = fixture.CreateContext(GlobexTenantId);

        List<string> acmeCodes = await acmeContext.SampleRecords.Select(record => record.Code).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> globexCodes = await globexContext.SampleRecords.Select(record => record.Code).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        acmeCodes.ShouldContain("ACME-CACHE");
        acmeCodes.ShouldNotContain("GLOBEX-CACHE");
        globexCodes.ShouldContain("GLOBEX-CACHE");
        globexCodes.ShouldNotContain("ACME-CACHE");
    }

    [Fact]
    public async Task RawSql_IsStillScopedByRowLevelSecurity()
    {
        await SeedAsync(AcmeTenantId, "ACME-RAW", "Acme raw probe");
        await SeedAsync(GlobexTenantId, "GLOBEX-RAW", "Globex raw probe");

        // A hand-written query with no tenant predicate at all: the database applies one anyway.
        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(GlobexTenantId);
        IEnumerable<Guid> tenantIds = await connection.QueryAsync<Guid>("SELECT TenantId FROM sample.SampleRecord");

        tenantIds.ShouldNotBeEmpty();
        tenantIds.Distinct().ShouldBe([GlobexTenantId]);
    }

    [Fact]
    public async Task RawSqlWithoutSessionContext_SeesNothing()
    {
        await SeedAsync(AcmeTenantId, "ACME-NOCTX", "Acme no-context probe");

        // No session context means no tenant, and the policy fails closed rather than open.
        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(tenantId: null);
        int visibleRows = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sample.SampleRecord");

        visibleRows.ShouldBe(0);
    }

    [Fact]
    public async Task InsertingARowForAnotherTenantViaRawSql_IsBlockedByThePolicy()
    {
        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(GlobexTenantId);

        SqlException exception = await Should.ThrowAsync<SqlException>(async () =>
            await connection.ExecuteAsync(
                """
                INSERT INTO sample.SampleRecord (Id, TenantId, Code, Title, IsActive, IsDeleted, CreatedBy, CreatedDate)
                VALUES (NEWID(), @tenantId, 'SMUGGLED', 'Planted row', 1, 0, NEWID(), SYSUTCDATETIME());
                """,
                new { tenantId = AcmeTenantId }));

        // Error 33504: the block predicate rejected the write.
        exception.Message.ShouldContain("block predicate", Case.Insensitive);
    }

    [Fact]
    public async Task SavingAnEntityStampedWithAnotherTenant_IsRefusedBeforeItReachesTheDatabase()
    {
        await using SampleDbContext acmeContext = fixture.CreateContext(AcmeTenantId);
        SampleRecord record = SampleRecord.Create("ACME-TAMPER", "Tamper probe");
        acmeContext.SampleRecords.Add(record);
        await acmeContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Simulate a handler that loads a row in one tenant and tries to move it to another.
        await using SampleDbContext tamperContext = fixture.CreateContext(AcmeTenantId);
        SampleRecord loaded = await tamperContext.SampleRecords.SingleAsync(candidate => candidate.Code == "ACME-TAMPER", cancellationToken: TestContext.Current.CancellationToken);
        tamperContext.Entry(loaded).Property(nameof(SampleRecord.TenantId)).CurrentValue = GlobexTenantId;

        await Should.ThrowAsync<TenantMismatchException>(async () => await tamperContext.SaveChangesAsync());
    }

    [Fact]
    public async Task NewRows_AreStampedWithTheTenantAndAuditColumns()
    {
        await using SampleDbContext context = fixture.CreateContext(AcmeTenantId);
        SampleRecord record = SampleRecord.Create("ACME-STAMP", "Stamp probe");

        // Nothing in the handler sets TenantId, CreatedBy or CreatedDate.
        context.SampleRecords.Add(record);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        record.TenantId.ShouldBe(AcmeTenantId);
        record.CreatedBy.ShouldNotBe(Guid.Empty);
        record.CreatedDate.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
        record.RowVersion.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeletingARow_SoftDeletesItAndHidesItFromQueries()
    {
        Guid recordId = await SeedAsync(AcmeTenantId, "ACME-SOFTDEL", "Soft delete probe");

        await using (SampleDbContext deleteContext = fixture.CreateContext(AcmeTenantId))
        {
            SampleRecord record = await deleteContext.SampleRecords.SingleAsync(candidate => candidate.Id == recordId, cancellationToken: TestContext.Current.CancellationToken);
            deleteContext.SampleRecords.Remove(record);
            await deleteContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using SampleDbContext readContext = fixture.CreateContext(AcmeTenantId);

        (await readContext.SampleRecords.AnyAsync(record => record.Id == recordId, cancellationToken: TestContext.Current.CancellationToken)).ShouldBeFalse();

        // Still on disk, because retention and audit depend on it.
        SampleRecord? archived = await readContext.SampleRecords
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .FirstOrDefaultAsync(record => record.Id == recordId, cancellationToken: TestContext.Current.CancellationToken);

        archived.ShouldNotBeNull();
        archived.IsDeleted.ShouldBeTrue();
        archived.DeletedDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task WaivingTheSoftDeleteFilter_DoesNotAlsoWaiveTenantIsolation()
    {
        Guid acmeRecordId = await SeedAsync(AcmeTenantId, "ACME-FILTERS", "Named filter probe");

        await using SampleDbContext globexContext = fixture.CreateContext(GlobexTenantId);

        // Named filters exist precisely so that "show me deleted rows too" cannot turn into
        // "show me every tenant's rows too".
        bool leaked = await globexContext.SampleRecords
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AnyAsync(record => record.Id == acmeRecordId, cancellationToken: TestContext.Current.CancellationToken);

        leaked.ShouldBeFalse();
    }

    [Fact]
    public async Task TheSameCodeCanBeUsedByDifferentTenants()
    {
        // Uniqueness is per tenant: two customers both having an "HR-01" is normal.
        await SeedAsync(AcmeTenantId, "SHARED-CODE", "Acme version");
        Guid globexId = await SeedAsync(GlobexTenantId, "SHARED-CODE", "Globex version");

        globexId.ShouldNotBe(Guid.Empty);
    }

    private async Task<Guid> SeedAsync(Guid tenantId, string code, string title)
    {
        await using SampleDbContext context = fixture.CreateContext(tenantId);

        SampleRecord? existing = await context.SampleRecords.FirstOrDefaultAsync(record => record.Code == code);
        if (existing is not null)
        {
            return existing.Id;
        }

        SampleRecord record = SampleRecord.Create(code, title);
        context.SampleRecords.Add(record);
        await context.SaveChangesAsync();
        return record.Id;
    }
}
