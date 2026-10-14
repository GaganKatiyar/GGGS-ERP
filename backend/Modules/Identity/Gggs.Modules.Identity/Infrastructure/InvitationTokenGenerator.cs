using System.Security.Cryptography;
using System.Text;
using Gggs.Modules.Identity.Application.Users;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// Generates invitation and password-reset tokens.
/// <para>
/// 32 bytes from a cryptographic generator, base64url encoded so it survives being pasted into a
/// link. Stored as a SHA-256 hash: the token is high-entropy and single-use, so it needs a hash
/// that resists lookup rather than the slow, salted hashing a password needs - and being fast
/// matters here because it is verified on a path that must not become a way to measure anything.
/// </para>
/// </summary>
public sealed class InvitationTokenGenerator : IInvitationTokenGenerator
{
    private const int TokenBytes = 32;

    public (string Token, string TokenHash) Generate()
    {
        string token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return (token, HashFor(token));
    }

    public string HashFor(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Base64url: no padding, and no characters a URL or an email client would mangle.</summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
