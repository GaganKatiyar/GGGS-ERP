using System.Text;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gggs.BuildingBlocks.Persistence;

/// <summary>
/// Builds the SQL Server row-level security objects that back tenant isolation at the database.
/// <para>
/// Every service database gets one predicate function and one security policy listing every
/// tenant-owned table. The filter predicate hides other tenants' rows from reads; the block
/// predicates stop a write from landing in, or being moved to, another tenant. Both read
/// <c>SESSION_CONTEXT('TenantId')</c>, which the connection interceptor sets as read-only.
/// </para>
/// <para>
/// Migrations and platform jobs connect as a member of the <c>rls_bypass</c> role. The application's
/// own login is never a member, and it does not own the tables either, because a table owner would
/// bypass the policy.
/// </para>
/// </summary>
public static class RowLevelSecurity
{
    public const string SecuritySchema = "security";

    public const string PredicateFunctionName = "fn_TenantAccessPredicate";

    public const string PolicyName = "TenantIsolationPolicy";

    public const string BypassRole = "rls_bypass";

    public static string CreateSecuritySchemaSql() =>
        $"""
        IF SCHEMA_ID(N'{SecuritySchema}') IS NULL EXEC(N'CREATE SCHEMA {SecuritySchema}');
        IF DATABASE_PRINCIPAL_ID(N'{BypassRole}') IS NULL CREATE ROLE [{BypassRole}];
        """;

    public static string CreatePredicateFunctionSql() =>
        $"""
        CREATE OR ALTER FUNCTION {SecuritySchema}.{PredicateFunctionName}(@TenantId uniqueidentifier)
        RETURNS TABLE
        WITH SCHEMABINDING
        AS
            RETURN
                SELECT 1 AS AccessResult
                WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                   OR IS_ROLEMEMBER(N'{BypassRole}') = 1;
        """;

    /// <summary>
    /// Creates (or replaces) the single policy covering every tenant-owned table.
    /// <para>
    /// The block predicates matter as much as the filter: without AFTER INSERT a compromised or
    /// buggy path could write rows tagged with another tenant, and without AFTER UPDATE it could
    /// move an existing row across the boundary.
    /// </para>
    /// </summary>
    public static string CreatePolicySql(IReadOnlyCollection<TenantTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Count == 0)
        {
            throw new ArgumentException("At least one table is required for the security policy.", nameof(tables));
        }

        var sql = new StringBuilder();
        sql.AppendLine(DropPolicySql());
        sql.AppendLine($"CREATE SECURITY POLICY {SecuritySchema}.{PolicyName}");

        var clauses = new List<string>(tables.Count * 3);
        foreach (TenantTable table in tables)
        {
            string target = $"[{table.Schema}].[{table.Table}]";
            string predicate = $"{SecuritySchema}.{PredicateFunctionName}([{table.TenantColumn}])";

            clauses.Add($"    ADD FILTER PREDICATE {predicate} ON {target}");
            clauses.Add($"    ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT");
            clauses.Add($"    ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE");
        }

        sql.AppendLine(string.Join($",{Environment.NewLine}", clauses));
        sql.AppendLine("WITH (STATE = ON, SCHEMABINDING = ON);");

        return sql.ToString();
    }

    public static string DropPolicySql() =>
        $"""
        IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'{PolicyName}' AND schema_id = SCHEMA_ID(N'{SecuritySchema}'))
            DROP SECURITY POLICY {SecuritySchema}.{PolicyName};
        """;

    /// <summary>
    /// Every table in the model whose entity is tenant-owned, in a stable order.
    /// <para>
    /// Works on a migration's <c>TargetModel</c> as well as a live one, through
    /// <see cref="TenantOwnedAnnotation"/>. Tables shared by several entity types (table splitting,
    /// TPH) appear once.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TenantTable> TenantTablesOf(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return
        [
            .. model.GetEntityTypes()
                .Where(entityType => !entityType.IsOwned() && entityType.GetTableName() is not null)
                .Where(IsTenantOwned)
                .Select(entityType => new TenantTable(entityType.GetSchema() ?? "dbo", entityType.GetTableName()!))
                .Distinct()
                .OrderBy(table => table.Schema, StringComparer.Ordinal)
                .ThenBy(table => table.Table, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Stamped on every tenant-owned entity type by <see cref="ModelBuilderExtensions"/>.
    /// <para>
    /// An annotation rather than a CLR-type check, because a migration's target model is rebuilt
    /// from the snapshot and has no CLR types to inspect. Nor can a <c>TenantId</c> column stand in
    /// for it: platform tables such as subscriptions carry one as a foreign key and must stay
    /// readable before any tenant is known.
    /// </para>
    /// </summary>
    public const string TenantOwnedAnnotation = "Gggs:TenantOwned";

    private static bool IsTenantOwned(IEntityType entityType)
        => entityType.FindAnnotation(TenantOwnedAnnotation)?.Value is true
            || (entityType.ClrType is { } clrType && typeof(ITenantOwned).IsAssignableFrom(clrType));

    /// <summary>A table covered by the policy.</summary>
    public sealed record TenantTable(string Schema, string Table, string TenantColumn = "TenantId");
}

/// <summary>Migration helpers so enabling RLS is one call in a migration.</summary>
public static class RowLevelSecurityMigrationExtensions
{
    /// <summary>
    /// Creates the security schema, bypass role and predicate function, then applies the policy to
    /// the given tables. Re-run it in a later migration whenever tenant-owned tables are added: the
    /// policy is replaced wholesale, so the list passed here must be the complete one.
    /// </summary>
    public static void EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        params RowLevelSecurity.TenantTable[] tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        // The existing policy goes first: it is schema-bound to the predicate function, and SQL
        // Server refuses to alter a function a policy still references - which is exactly what
        // every migration after the first one does.
        migrationBuilder.Sql(RowLevelSecurity.DropPolicySql());
        migrationBuilder.Sql(RowLevelSecurity.CreateSecuritySchemaSql());
        migrationBuilder.Sql(RowLevelSecurity.CreatePredicateFunctionSql());
        migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(tables));
    }

    /// <summary>
    /// Brings the policy in line with the model: every tenant-owned table in
    /// <paramref name="targetModel"/> is covered, and nothing else.
    /// <para>
    /// Call it at the end of any migration that adds or removes a tenant-owned table, passing the
    /// migration's own <c>TargetModel</c>. The list is derived rather than typed out, which removes
    /// the failure where a new table is forgotten and silently left without the database backstop.
    /// </para>
    /// </summary>
    public static void SyncTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, IModel targetModel)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(targetModel);

        migrationBuilder.EnableTenantRowLevelSecurity([.. RowLevelSecurity.TenantTablesOf(targetModel)]);
    }

    public static void DisableTenantRowLevelSecurity(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(RowLevelSecurity.DropPolicySql());
    }
}
