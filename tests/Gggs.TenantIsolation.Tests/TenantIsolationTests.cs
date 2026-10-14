using Dapper;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Roles;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.TenantIsolation.Tests;

/// <summary>
/// Proof that a tenant cannot reach another tenant's rows, exercised against a real SQL Server
/// holding the real PeopleOS schema, with the production filters, interceptors and security policy
/// in place. <c>iam.Role</c> is the probe: an ordinary tenant-owned, soft-deletable table.
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

        await using AppDbContext globexContext = fixture.CreateContext(GlobexTenantId);
        List<Role> visible = await globexContext.Set<Role>().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        visible.ShouldNotBeEmpty();
        visible.ShouldAllBe(record => record.TenantId == GlobexTenantId);
        visible.Any(record => record.Id == acmeRecordId).ShouldBeFalse();
    }

    [Fact]
    public async Task FetchingAnotherTenantsRecordById_FindsNothing()
    {
        Guid acmeRecordId = await SeedAsync(AcmeTenantId, "ACME-BYID", "Only Acme may see this");

        await using AppDbContext globexContext = fixture.CreateContext(GlobexTenantId);
        Role? stolen = await globexContext.Set<Role>()
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

        await using AppDbContext acmeContext = fixture.CreateContext(AcmeTenantId);
        await using AppDbContext globexContext = fixture.CreateContext(GlobexTenantId);

        List<string> acmeCodes = await acmeContext.Set<Role>().Select(record => record.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        List<string> globexCodes = await globexContext.Set<Role>().Select(record => record.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

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
        IEnumerable<Guid> tenantIds = await connection.QueryAsync<Guid>("SELECT TenantId FROM iam.Role");

        tenantIds.ShouldNotBeEmpty();
        tenantIds.Distinct().ShouldBe([GlobexTenantId]);
    }

    [Fact]
    public async Task RawSqlWithoutSessionContext_SeesNothing()
    {
        await SeedAsync(AcmeTenantId, "ACME-NOCTX", "Acme no-context probe");

        // No session context means no tenant, and the policy fails closed rather than open.
        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(tenantId: null);
        int visibleRows = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM iam.Role");

        visibleRows.ShouldBe(0);
    }

    [Fact]
    public async Task InsertingARowForAnotherTenantViaRawSql_IsBlockedByThePolicy()
    {
        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(GlobexTenantId);

        SqlException exception = await Should.ThrowAsync<SqlException>(async () =>
            await connection.ExecuteAsync(
                """
                INSERT INTO iam.Role (Id, TenantId, Name, NormalizedName, IsSystem, IsActive, IsDeleted, CreatedBy, CreatedDate)
                VALUES (NEWID(), @tenantId, 'SMUGGLED', 'SMUGGLED', 0, 1, 0, NEWID(), SYSUTCDATETIME());
                """,
                new { tenantId = AcmeTenantId }));

        // Error 33504: the block predicate rejected the write.
        exception.Message.ShouldContain("block predicate", Case.Insensitive);
    }

    [Fact]
    public async Task SavingAnEntityStampedWithAnotherTenant_IsRefusedBeforeItReachesTheDatabase()
    {
        await using AppDbContext acmeContext = fixture.CreateContext(AcmeTenantId);
        Role record = Role.Create("ACME-TAMPER", "Tamper probe");
        acmeContext.Set<Role>().Add(record);
        await acmeContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Simulate a handler that loads a row in one tenant and tries to move it to another.
        await using AppDbContext tamperContext = fixture.CreateContext(AcmeTenantId);
        Role loaded = await tamperContext.Set<Role>().SingleAsync(candidate => candidate.Name == "ACME-TAMPER", cancellationToken: TestContext.Current.CancellationToken);
        tamperContext.Entry(loaded).Property(nameof(Role.TenantId)).CurrentValue = GlobexTenantId;

        await Should.ThrowAsync<TenantMismatchException>(async () => await tamperContext.SaveChangesAsync());
    }

    [Fact]
    public async Task NewRows_AreStampedWithTheTenantAndAuditColumns()
    {
        await using AppDbContext context = fixture.CreateContext(AcmeTenantId);
        Role record = Role.Create("ACME-STAMP", "Stamp probe");

        // Nothing in the handler sets TenantId, CreatedBy or CreatedDate.
        context.Set<Role>().Add(record);
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

        await using (AppDbContext deleteContext = fixture.CreateContext(AcmeTenantId))
        {
            Role record = await deleteContext.Set<Role>().SingleAsync(candidate => candidate.Id == recordId, cancellationToken: TestContext.Current.CancellationToken);
            deleteContext.Set<Role>().Remove(record);
            await deleteContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AppDbContext readContext = fixture.CreateContext(AcmeTenantId);

        (await readContext.Set<Role>().AnyAsync(record => record.Id == recordId, cancellationToken: TestContext.Current.CancellationToken)).ShouldBeFalse();

        // Still on disk, because retention and audit depend on it.
        Role? archived = await readContext.Set<Role>()
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

        await using AppDbContext globexContext = fixture.CreateContext(GlobexTenantId);

        // Named filters exist precisely so that "show me deleted rows too" cannot turn into
        // "show me every tenant's rows too".
        bool leaked = await globexContext.Set<Role>()
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

    [Fact]
    public async Task TheSecurityPolicy_CoversExactlyTheModelsTenantOwnedTables()
    {
        // The policy is generated from the model by the migrations. This checks the database that
        // those migrations actually built: every tenant-owned table has a filter predicate and both
        // block predicates, and no platform table is caught by mistake.
        await using AppDbContext context = fixture.CreateContext(AcmeTenantId);
        List<string> expected =
        [
            .. RowLevelSecurity.TenantTablesOf(context.Model).Select(table => $"{table.Schema}.{table.Table}"),
        ];

        await using SqlConnection connection = await fixture.OpenRawConnectionAsync(tenantId: null);
        List<(string Table, string? Operation)> predicates =
        [
            .. await connection.QueryAsync<(string Table, string? Operation)>(
                """
                SELECT OBJECT_SCHEMA_NAME(target_object_id) + '.' + OBJECT_NAME(target_object_id), operation_desc
                FROM sys.security_predicates
                """),
        ];

        predicates.Select(predicate => predicate.Table).Distinct().Order().ShouldBe(expected.Order());

        foreach (string table in expected)
        {
            List<string?> operations = [.. predicates.Where(predicate => predicate.Table == table).Select(predicate => predicate.Operation)];
            operations.ShouldContain((string?)null, $"{table} has no filter predicate");
            operations.ShouldContain("AFTER INSERT", $"{table} has no insert block predicate");
            operations.ShouldContain("AFTER UPDATE", $"{table} has no update block predicate");
        }
    }

    private async Task<Guid> SeedAsync(Guid tenantId, string code, string title)
    {
        await using AppDbContext context = fixture.CreateContext(tenantId);

        Role? existing = await context.Set<Role>().FirstOrDefaultAsync(record => record.Name == code);
        if (existing is not null)
        {
            return existing.Id;
        }

        Role record = Role.Create(code, title);
        context.Set<Role>().Add(record);
        await context.SaveChangesAsync();
        return record.Id;
    }
}
