using System.Security.Cryptography;
using System.Text;
using Gggs.Time.Application.Attendance;

namespace Gggs.Time.Infrastructure;

/// <summary>Same shape as Identity's <c>InvitationTokenGenerator</c> - see <see cref="IApiKeyGenerator"/>'s
/// own remarks on why a fast hash is the right one here, not a slow/salted password hash.</summary>
public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    private const int KeyBytes = 32;
    private const string KeyPrefix = "gggs_dev_";

    public (string ApiKey, string ApiKeyHash) Generate()
    {
        string apiKey = KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyBytes));
        return (apiKey, HashFor(apiKey));
    }

    public string HashFor(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
