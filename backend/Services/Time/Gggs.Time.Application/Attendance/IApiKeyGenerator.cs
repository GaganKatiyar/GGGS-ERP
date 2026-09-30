namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Generates and verifies a <see cref="Domain.Attendance.BiometricDevice"/>'s API key - the same
/// "32 random bytes, SHA-256 hash stored, never the key itself" shape Identity's
/// <c>IInvitationTokenGenerator</c> already uses for invitation/reset tokens, one level down: an API
/// key is shown to the operator exactly once at registration/rotation time, and only its hash is
/// ever persisted or compared against again.
/// </summary>
public interface IApiKeyGenerator
{
    (string ApiKey, string ApiKeyHash) Generate();

    string HashFor(string apiKey);
}
