using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Platform.Console;
using Gggs.Modules.Platform.Domain.Console;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>A dedicated database's details, and the connection string they make.</summary>
public sealed class DedicatedDatabaseTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASqlLogin_NeedsAUser()
    {
        DedicatedDatabase database = DedicatedDatabase.For(Guid.NewGuid());

        Should.Throw<DomainRuleException>(() => database.Configure("sql.example.com", "nb", DatabaseAuthentication.SqlLogin, " ", DatabaseEncryption.Strict, Now, "Staff"))
            .ErrorCode.ShouldBe("DATABASE_USER_REQUIRED");
    }

    [Fact]
    public void SwitchingToASignInWithoutAPassword_ForgetsTheStoredOne()
    {
        DedicatedDatabase database = DedicatedDatabase.For(Guid.NewGuid());
        database.Configure("sql.example.com", "nb", DatabaseAuthentication.SqlLogin, "nb_app", DatabaseEncryption.Strict, Now, "Staff");
        database.SetPassword("ciphertext", Now, "Staff");

        database.Configure("sql.example.com", "nb", DatabaseAuthentication.ManagedIdentity, null, DatabaseEncryption.Strict, Now, "Staff");

        database.HasPassword.ShouldBeFalse();
        database.UserName.ShouldBeNull();
        Should.Throw<DomainRuleException>(() => database.SetPassword("ciphertext", Now, "Staff")).ErrorCode.ShouldBe("DATABASE_PASSWORD_NOT_USED");
    }

    [Fact]
    public void TheConnectionString_FollowsTheSignInAndEncryptionChoices()
    {
        string sql = OrganisationDatabaseConsole.BuildConnectionString("sql.example.com,1433", "nb_gggs", DatabaseAuthentication.SqlLogin, "nb_app", "pw", DatabaseEncryption.Strict);
        sql.ShouldContain("Data Source=sql.example.com,1433");
        sql.ShouldContain("Initial Catalog=nb_gggs");
        sql.ShouldContain("User ID=nb_app");
        sql.ShouldContain("Password=pw");
        sql.ShouldContain("Encrypt=True");
        sql.ShouldContain("Trust Server Certificate=False");

        string integrated = OrganisationDatabaseConsole.BuildConnectionString("localhost", "nb", DatabaseAuthentication.Integrated, "ignored", "ignored", DatabaseEncryption.TrustCertificate);
        integrated.ShouldContain("Integrated Security=True");
        integrated.ShouldContain("Trust Server Certificate=True");
        integrated.ShouldNotContain("Password");

        OrganisationDatabaseConsole.BuildConnectionString("db.example.com", "nb", DatabaseAuthentication.ManagedIdentity, null, null, DatabaseEncryption.Strict)
            .ShouldContain("Authentication=ActiveDirectoryManagedIdentity");
    }
}
