using System.Text;
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

        migrationBuilder.Sql(RowLevelSecurity.CreateSecuritySchemaSql());
        migrationBuilder.Sql(RowLevelSecurity.CreatePredicateFunctionSql());
        migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(tables));
    }

    public static void DisableTenantRowLevelSecurity(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(RowLevelSecurity.DropPolicySql());
    }
}
