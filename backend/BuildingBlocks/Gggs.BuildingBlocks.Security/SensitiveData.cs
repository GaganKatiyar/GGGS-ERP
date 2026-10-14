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
}

/// <summary>The purposes in use. Released values: renaming one makes existing ciphertext unreadable.</summary>
public static class SensitiveDataPurposes
{
    public const string Pan = "employee.pan";
    public const string Uan = "employee.uan";
    public const string Esic = "employee.esic";
    public const string NationalId = "employee.national-id";
    public const string BankAccount = "employee.bank-account";
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
}

public static class SensitiveDataExtensions
{
    /// <summary>
    /// The application name every host shares. Data Protection isolates keys per application by
    /// default, and the API and the worker are different applications on disk - without this, a
    /// value the worker encrypted could not be read by the API.
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
