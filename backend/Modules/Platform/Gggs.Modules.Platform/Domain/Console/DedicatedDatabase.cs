using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Platform.Domain.Console;

/// <summary>How the application signs in to an organisation's dedicated database.</summary>
public enum DatabaseAuthentication
{
    /// <summary>A SQL Server login: user and password.</summary>
    SqlLogin = 1,

    /// <summary>The server process's own Windows identity.</summary>
    Integrated = 2,

    /// <summary>The server's Azure managed identity.</summary>
    ManagedIdentity = 3,
}

/// <summary>Whether the connection is encrypted, and whether the server's certificate is checked.</summary>
public enum DatabaseEncryption
{
    Strict = 1,

    /// <summary>Encrypted, but any certificate is accepted. Development only.</summary>
    TrustCertificate = 2,

    None = 3,
}

/// <summary>
/// The connection details of an organisation's dedicated database, kept by the platform console.
/// <para>
/// Platform-owned: no organisation reads its own row. The password is stored only as ciphertext
/// (<c>SensitiveDataPurposes.TenantDatabasePassword</c>), so this entity never holds it in clear.
/// </para>
/// <para>
/// Recording the details does not move the organisation: the HR application keeps reading and writing
/// it in the shared database until dedicated hosting is switched on.
/// </para>
/// </summary>
public sealed class DedicatedDatabase : AggregateRoot, IPlatformOwned
{
    private DedicatedDatabase()
    {
        Server = null!;
        DatabaseName = null!;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Host name, optionally followed by a comma and the port.</summary>
    public string Server { get; private set; }

    public string DatabaseName { get; private set; }

    public DatabaseAuthentication Authentication { get; private set; }

    public string? UserName { get; private set; }

    /// <summary>Ciphertext. Null when the sign-in method needs no password.</summary>
    public string? PasswordCiphertext { get; private set; }

    public DatabaseEncryption Encryption { get; private set; }

    public DateTime? PasswordChangedDate { get; private set; }

    public string? PasswordChangedBy { get; private set; }

    public DateTime UpdatedDate { get; private set; }

    public string UpdatedBy { get; private set; } = null!;

    public DateTime? LastTestedDate { get; private set; }

    public bool? LastTestSucceeded { get; private set; }

    public string? LastTestResult { get; private set; }

    public bool HasPassword => PasswordCiphertext is not null;

    public static DedicatedDatabase For(Guid tenantId) => new() { Id = SequentialGuid.Create(), TenantId = Guard.NotEmpty(tenantId) };

    /// <summary>
    /// Sets everything but the password. Switching to a method that takes no password forgets the
    /// stored one, so a stale credential is not kept for nothing.
    /// </summary>
    public void Configure(string server, string databaseName, DatabaseAuthentication authentication, string? userName, DatabaseEncryption encryption, DateTime now, string staffName)
    {
        Server = Guard.MaxLength(Guard.NotNullOrWhiteSpace(server).Trim(), 256);
        DatabaseName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(databaseName).Trim(), 128);
        Guard.Against(!Enum.IsDefined(authentication), "DATABASE_AUTHENTICATION_UNKNOWN", "Choose how to sign in to the database.");
        Guard.Against(!Enum.IsDefined(encryption), "DATABASE_ENCRYPTION_UNKNOWN", "Choose whether the connection is encrypted.");
        Authentication = authentication;
        Encryption = encryption;

        if (authentication == DatabaseAuthentication.SqlLogin)
        {
            Guard.Against(string.IsNullOrWhiteSpace(userName), "DATABASE_USER_REQUIRED", "Enter the SQL Server login to sign in with.");
            UserName = Guard.MaxLength(userName!.Trim(), 128);
        }
        else
        {
            UserName = null;
            PasswordCiphertext = null;
        }

        UpdatedDate = now;
        UpdatedBy = Guard.MaxLength(staffName, 200);
    }

    public void SetPassword(string ciphertext, DateTime now, string staffName)
    {
        Guard.Against(Authentication != DatabaseAuthentication.SqlLogin, "DATABASE_PASSWORD_NOT_USED", "This sign-in method does not use a password.");
        PasswordCiphertext = Guard.NotNullOrWhiteSpace(ciphertext);
        PasswordChangedDate = now;
        PasswordChangedBy = Guard.MaxLength(staffName, 200);
    }

    public void RecordTest(DateTime now, bool succeeded, string result)
    {
        LastTestedDate = now;
        LastTestSucceeded = succeeded;
        LastTestResult = Guard.MaxLength(result, 500);
    }
}
