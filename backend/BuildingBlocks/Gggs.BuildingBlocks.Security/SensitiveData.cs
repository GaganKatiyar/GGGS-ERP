using Gggs.BuildingBlocks.Domain.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Encrypts sensitive field values for storage and reveals them for callers allowed to see them.
/// <para>
/// One purpose string per kind of value, so a ciphertext lifted from the PAN column cannot be
/// replayed into the bank-account column and decrypted there.
/// </para>
/// </summary>
public interface ISensitiveDataProtector
{
    ProtectedValue Protect(string purpose, string plain, int visibleCharacters = 4);

    string Reveal(string purpose, ProtectedValue value);

    /// <summary>Encrypts a longer block of text that has no masked form to show, such as a saved form.</summary>
    string ProtectText(string purpose, string plain);

    string RevealText(string purpose, string ciphertext);
}

/// <summary>The purposes in use. Released values: renaming one makes existing ciphertext unreadable.</summary>
public static class SensitiveDataPurposes
{
    public const string Pan = "employee.pan";
    public const string Uan = "employee.uan";
    public const string Esic = "employee.esic";
    public const string NationalId = "employee.national-id";
    public const string Passport = "employee.passport";
    public const string BankAccount = "employee.bank-account";

    /// <summary>A half-filled Hire form, kept whole: it holds Aadhaar, PAN, passport and bank details.</summary>
    public const string HireDraft = "employee.hire-draft";

    /// <summary>The password of an organisation's dedicated database, set in the platform console.</summary>
    public const string TenantDatabasePassword = "platform.tenant-database-password";
}

/// <summary>ASP.NET Data Protection, which handles key rotation and, per profile, key storage.</summary>
internal sealed class DataProtectionSensitiveDataProtector(IDataProtectionProvider provider) : ISensitiveDataProtector
{
    private const string RootPurpose = "Gggs.SensitiveData.v1";

    public ProtectedValue Protect(string purpose, string plain, int visibleCharacters = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(plain);

        string normalized = plain.Trim();
        return new ProtectedValue(
            provider.CreateProtector(RootPurpose, purpose).Protect(normalized),
            ProtectedValue.MaskOf(normalized, visibleCharacters));
    }

    public string Reveal(string purpose, ProtectedValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(value);

        return provider.CreateProtector(RootPurpose, purpose).Unprotect(value.Ciphertext);
    }

    public string ProtectText(string purpose, string plain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(plain);
        return provider.CreateProtector(RootPurpose, purpose).Protect(plain);
    }

    public string RevealText(string purpose, string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return provider.CreateProtector(RootPurpose, purpose).Unprotect(ciphertext);
    }
}

public static class SensitiveDataExtensions
{
    /// <summary>
    /// The application name every host shares. Data Protection isolates keys per application by
    /// default, and the API and the worker are different applications on disk - without this, a
    /// value the worker encrypted could not be read by the API.
    /// <para>
    /// Keeps the product's old name on purpose: the keys are bound to this exact value, so changing it
    /// would leave every saved secret and encrypted field unreadable.
    /// </para>
    /// </summary>
    public const string ApplicationName = "PeopleOS";

    /// <summary>
    /// Registers field encryption. Key storage follows the deployment: on-premise the key ring sits
    /// on the persistent volume both containers mount (infra/onprem); in the cloud it must be
    /// persisted to shared storage and protected with Key Vault before production data is stored.
    /// </summary>
    public static IServiceCollection AddGggsSensitiveData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.AddSingleton<ISensitiveDataProtector, DataProtectionSensitiveDataProtector>();
        return services;
    }
}
