using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Gggs.Modules.Identity.Application.Authentication;

/// <summary>
/// Time-based one-time passwords (RFC 6238 over RFC 4226): six digits from HMAC-SHA1 over a 30-second
/// time step, which is what every authenticator app (Microsoft, Google, Authy, 1Password) implements.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;

    public const int Digits = 6;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new 160-bit secret, as the authenticator app shows and stores it (base32).</summary>
    public static string NewSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The code for a step.</summary>
    public static string Code(string base32Secret, long step)
    {
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        // SHA-1 is what RFC 6238 and every authenticator app use; HMAC-SHA1 is not affected by SHA-1 collisions.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(FromBase32(base32Secret));
#pragma warning restore CA5350
        byte[] hash = hmac.ComputeHash(counter);
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The step a code is valid for - allowing one step of clock drift either way - or null. A step at or
    /// before <paramref name="lastUsedStep"/> is refused, so a code seen over a shoulder cannot be replayed.
    /// </summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        string digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != Digits)
        {
            return null;
        }

        long current = StepAt(now);
        for (long step = current - 1; step <= current + 1; step++)
        {
            if (step > lastUsedStep && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>The otpauth:// link an authenticator app scans (or opens) to add the account.</summary>
    public static string ProvisioningUri(string issuer, string account, string base32Secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>Ten one-time recovery codes in the form ABCD-EFGH.</summary>
    public static IReadOnlyList<string> NewRecoveryCodes()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return [.. Enumerable.Range(0, 10).Select(_ =>
        {
            string raw = new([.. Enumerable.Range(0, 8).Select(_ => letters[RandomNumberGenerator.GetInt32(letters.Length)])]);
            return $"{raw[..4]}-{raw[4..]}";
        })];
    }

    /// <summary>How recovery codes are stored: SHA-256 of the normalised code.</summary>
    public static string HashRecoveryCode(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((code ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant())));

    public static string ToBase32(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var output = new StringBuilder();
        int buffer = 0;
        int bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        string clean = (text ?? string.Empty).TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0;
        int bits = 0;
        foreach (char c in clean)
        {
            int value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("The secret is not valid base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
