using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// Password hashing. Short, because most of what matters here is a property rather than a case:
/// the same password must never produce the same stored value twice, and a wrong password must
/// never verify.
/// </summary>
public sealed class PasswordHashingTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new(NullLogger<Pbkdf2PasswordHasher>.Instance);

    [Fact]
    public void ACorrectPasswordVerifies()
    {
        string hash = Hasher.Hash("correct horse battery staple");

        Hasher.Verify(hash, "correct horse battery staple").ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void AWrongPasswordDoesNotVerify()
    {
        string hash = Hasher.Hash("correct horse battery staple");

        Hasher.Verify(hash, "correct horse battery stapler").ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void TheSamePasswordHashesDifferentlyEveryTime()
    {
        // Each hash carries its own salt. Without that, identical passwords would be visible as
        // identical stored values, and one cracked hash would reveal every user who shared it.
        string first = Hasher.Hash("same password");
        string second = Hasher.Hash("same password");

        first.ShouldNotBe(second);
        Hasher.Verify(first, "same password").ShouldBe(PasswordVerificationResult.Success);
        Hasher.Verify(second, "same password").ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void AMalformedStoredHashFailsRatherThanThrowing()
    {
        // A corrupted or hand-edited row must refuse the sign-in, not take the endpoint down.
        Hasher.Verify("not-a-hash", "anything").ShouldBe(PasswordVerificationResult.Failed);
        Hasher.Verify(string.Empty, "anything").ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void AnEmptyProvidedPasswordNeverVerifies()
    {
        string hash = Hasher.Hash("a real password");

        Hasher.Verify(hash, string.Empty).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void LongPassphrasesAreSupported()
    {
        // The policy encourages passphrases, so the hasher must not quietly truncate them the way
        // some older algorithms do.
        string passphrase = new string('a', 200) + "-tail";
        string hash = Hasher.Hash(passphrase);

        Hasher.Verify(hash, passphrase).ShouldBe(PasswordVerificationResult.Success);
        Hasher.Verify(hash, new string('a', 200)).ShouldBe(PasswordVerificationResult.Failed);
    }
}
