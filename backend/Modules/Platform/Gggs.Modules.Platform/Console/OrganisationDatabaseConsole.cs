using System.Diagnostics;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Console;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gggs.Modules.Platform.Console;

/// <summary>The shared database an organisation is in today, as far as staff may see it: no credentials.</summary>
public sealed record SharedDatabaseView(string Server, string DatabaseName, int OtherOrganisations, Guid TenantId, int? SizeMegabytes);

public sealed record DedicatedDatabaseView(
    string Server,
    string DatabaseName,
    DatabaseAuthentication Authentication,
    string? UserName,
    DatabaseEncryption Encryption,
    bool HasPassword,
    DateTime? PasswordChangedDate,
    string? PasswordChangedBy,
    DateTime UpdatedDate,
    string UpdatedBy,
    DateTime? LastTestedDate,
    bool? LastTestSucceeded,
    string? LastTestResult,
    string ConnectionStringPreview);

/// <summary>
/// The Database tab. <see cref="InUse"/> says where the HR application actually reads and writes the
/// organisation - always "Shared" until dedicated hosting is switched on.
/// </summary>
public sealed record OrganisationDatabaseView(Guid TenantId, string InUse, SharedDatabaseView Shared, DedicatedDatabaseView? Dedicated);

/// <summary>A dedicated database's details as the form sends them. A null password keeps the stored one.</summary>
public sealed record SaveDatabaseBody(
    string Server,
    string DatabaseName,
    DatabaseAuthentication Authentication,
    string? UserName,
    string? Password,
    DatabaseEncryption Encryption = DatabaseEncryption.Strict);

public sealed record DatabaseTestResult(bool Succeeded, int ElapsedMilliseconds, string? ServerVersion, string? Schema, string Message);

public sealed record RevealDatabasePasswordBody(string Reason);

public sealed record RevealedDatabasePassword(string Password, DateTime HideAfter);

/// <summary>
/// Where each organisation's data is stored, for platform staff administrators: the shared database it
/// is in, and the details of a dedicated database kept for it. The password is encrypted at rest, never
/// returned except by an explicit reveal, and every reveal, test and change is in the platform audit trail.
/// </summary>
public sealed class OrganisationDatabaseConsole(
    AppDbContext dbContext,
    ISensitiveDataProtector protector,
    PlatformAuditWriter audit,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan RevealFor = TimeSpan.FromSeconds(60);

    public async Task<OrganisationDatabaseView> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await TenantAsync(tenantId, cancellationToken);
        DedicatedDatabase? dedicated = await dbContext.DedicatedDatabases.AsNoTracking()
            .FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);

        return new OrganisationDatabaseView(
            tenantId,
            "Shared",
            await SharedAsync(tenantId, cancellationToken),
            dedicated is null ? null : ToView(dedicated));
    }

    public async Task<OrganisationDatabaseView> SaveAsync(Guid tenantId, SaveDatabaseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Tenant tenant = await TenantAsync(tenantId, cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        DedicatedDatabase? database = await dbContext.DedicatedDatabases.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        bool created = database is null;
        if (database is null)
        {
            database = DedicatedDatabase.For(tenantId);
            dbContext.DedicatedDatabases.Add(database);
        }

        database.Configure(body.Server, body.DatabaseName, body.Authentication, body.UserName, body.Encryption, now, audit.StaffName);

        bool passwordChanged = false;
        if (body.Authentication == DatabaseAuthentication.SqlLogin)
        {
            if (!string.IsNullOrEmpty(body.Password))
            {
                database.SetPassword(protector.ProtectText(SensitiveDataPurposes.TenantDatabasePassword, body.Password), now, audit.StaffName);
                passwordChanged = true;
            }
            else if (!database.HasPassword)
            {
                throw new DomainRuleException("DATABASE_PASSWORD_REQUIRED", "Enter the password for the SQL Server login.");
            }
        }

        audit.Record(
            created ? "database.details-added" : "database.details-changed",
            $"Dedicated database {(created ? "set" : "changed")}: {database.DatabaseName} on {database.Server}{(passwordChanged ? "; password replaced" : string.Empty)}",
            tenant.Id,
            tenant.Code);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task RemoveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Tenant tenant = await TenantAsync(tenantId, cancellationToken);
        DedicatedDatabase? database = await dbContext.DedicatedDatabases.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        if (database is null)
        {
            return;
        }

        dbContext.DedicatedDatabases.Remove(database);
        audit.Record("database.details-removed", $"Dedicated database details removed ({database.DatabaseName} on {database.Server})", tenant.Id, tenant.Code);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tries the connection: the form's values, with the stored password when the form leaves it blank.
    /// Only sign-in, the server version and the schema version are read - never any data.
    /// </summary>
    public async Task<DatabaseTestResult> TestAsync(Guid tenantId, SaveDatabaseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Tenant tenant = await TenantAsync(tenantId, cancellationToken);
        DedicatedDatabase? stored = await dbContext.DedicatedDatabases.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);

        string? password = body.Password;
        bool usesStoredPassword = string.IsNullOrEmpty(password) && body.Authentication == DatabaseAuthentication.SqlLogin;
        if (usesStoredPassword && stored?.PasswordCiphertext is { } ciphertext)
        {
            password = protector.RevealText(SensitiveDataPurposes.TenantDatabasePassword, ciphertext);
        }

        DatabaseTestResult result = await ProbeAsync(BuildConnectionString(body.Server, body.DatabaseName, body.Authentication, body.UserName, password, body.Encryption), cancellationToken);

        bool matchesStored = stored is not null
            && usesStoredPassword == (body.Authentication == DatabaseAuthentication.SqlLogin)
            && string.Equals(stored.Server, body.Server?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(stored.DatabaseName, body.DatabaseName?.Trim(), StringComparison.OrdinalIgnoreCase)
            && stored.Authentication == body.Authentication
            && string.Equals(stored.UserName, body.UserName?.Trim(), StringComparison.OrdinalIgnoreCase)
            && stored.Encryption == body.Encryption;
        if (matchesStored)
        {
            stored!.RecordTest(timeProvider.GetUtcNow().UtcDateTime, result.Succeeded, result.Message);
        }

        audit.Record("database.tested", $"Connection tested ({body.DatabaseName} on {body.Server}): {(result.Succeeded ? "connected" : "failed")}", tenant.Id, tenant.Code);
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RevealedDatabasePassword> RevealAsync(Guid tenantId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleException("REVEAL_REASON_REQUIRED", "Say why you need to see the password; it is recorded.");
        }

        Tenant tenant = await TenantAsync(tenantId, cancellationToken);
        DedicatedDatabase database = await dbContext.DedicatedDatabases.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken)
            ?? throw new NotFoundException("Dedicated database", tenantId);
        if (database.PasswordCiphertext is null)
        {
            throw new DomainRuleException("DATABASE_NO_PASSWORD", "No password is stored for this database.");
        }

        string password = protector.RevealText(SensitiveDataPurposes.TenantDatabasePassword, database.PasswordCiphertext);
        audit.Record("database.password-revealed", $"Database password revealed ({database.DatabaseName} on {database.Server})", tenant.Id, tenant.Code, reason.Trim());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RevealedDatabasePassword(password, timeProvider.GetUtcNow().UtcDateTime.Add(RevealFor));
    }

    /// <summary>The connection string the details make. <paramref name="password"/> null leaves it out.</summary>
    public static string BuildConnectionString(string? server, string? databaseName, DatabaseAuthentication authentication, string? userName, string? password, DatabaseEncryption encryption)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server?.Trim() ?? string.Empty,
            InitialCatalog = databaseName?.Trim() ?? string.Empty,
            Encrypt = encryption == DatabaseEncryption.None ? SqlConnectionEncryptOption.Optional : SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = encryption == DatabaseEncryption.TrustCertificate,
            ConnectTimeout = 8,
            ApplicationName = "Nuts and Bolts platform console",
        };

        switch (authentication)
        {
            case DatabaseAuthentication.SqlLogin:
                builder.UserID = userName?.Trim() ?? string.Empty;
                if (password is not null)
                {
                    builder.Password = password;
                }

                break;
            case DatabaseAuthentication.Integrated:
                builder.IntegratedSecurity = true;
                break;
            case DatabaseAuthentication.ManagedIdentity:
                builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
                break;
        }

        return builder.ConnectionString;
    }

    private async Task<DatabaseTestResult> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            string version = connection.ServerVersion;

            string? latestApplied = null;
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    $"IF OBJECT_ID(N'[{AppDbContext.MigrationsHistorySchema}].[__EFMigrationsHistory]') IS NOT NULL " +
                    $"SELECT TOP 1 MigrationId FROM [{AppDbContext.MigrationsHistorySchema}].[__EFMigrationsHistory] ORDER BY MigrationId DESC";
                latestApplied = await command.ExecuteScalarAsync(cancellationToken) as string;
            }

            string? latestKnown = dbContext.Database.GetMigrations().LastOrDefault();
            string schema = latestApplied is null
                ? "Empty: the application's tables have not been created"
                : string.Equals(latestApplied, latestKnown, StringComparison.Ordinal)
                    ? $"Up to date (migration {latestApplied})"
                    : $"Behind: last migration {latestApplied}, the application is at {latestKnown}";

            int elapsed = (int)clock.ElapsedMilliseconds;
            return new DatabaseTestResult(true, elapsed, version, schema, $"Connected in {elapsed} ms · SQL Server {version} · {schema}");
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            int elapsed = (int)clock.ElapsedMilliseconds;
            string reason = exception.Message.Split('\n')[0].Trim();
            return new DatabaseTestResult(false, elapsed, null, null, $"Could not connect: {reason}");
        }
    }

    private async Task<SharedDatabaseView> SharedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString(DataExtensions.ConnectionStringName) ?? string.Empty);
        // Every organisation is in the shared database today, dedicated details or not.
        int others = await dbContext.Tenants.CountAsync(tenant => tenant.Id != tenantId, cancellationToken);

        int? size = null;
        try
        {
            size = await dbContext.Database
                .SqlQueryRaw<int>("SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024 AS int) AS [Value] FROM sys.database_files")
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // Without VIEW DEFINITION the size is simply not shown.
        }

        return new SharedDatabaseView(builder.DataSource, builder.InitialCatalog, others, tenantId, size);
    }

    private static DedicatedDatabaseView ToView(DedicatedDatabase database) => new(
        database.Server,
        database.DatabaseName,
        database.Authentication,
        database.UserName,
        database.Encryption,
        database.HasPassword,
        database.PasswordChangedDate,
        database.PasswordChangedBy,
        database.UpdatedDate,
        database.UpdatedBy,
        database.LastTestedDate,
        database.LastTestSucceeded,
        database.LastTestResult,
        BuildConnectionString(database.Server, database.DatabaseName, database.Authentication, database.UserName, database.HasPassword ? "********" : null, database.Encryption));

    private async Task<Tenant> TenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException("Organisation", tenantId);
}
