using Gggs.BuildingBlocks.Application;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Domain.PlatformStaff;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.PlatformStaff;

/// <summary>The outcome of a platform staff sign-in attempt. Shaped like <c>SignInResult</c> deliberately.</summary>
public sealed record PlatformStaffSignInResult
{
    public required bool Succeeded { get; init; }

    public SignInFailureReason FailureReason { get; init; }

    public Guid? PlatformStaffId { get; init; }

    public DateTime? LockoutEndDate { get; init; }

    public static PlatformStaffSignInResult Success(Guid platformStaffId) => new()
    {
        Succeeded = true,
        PlatformStaffId = platformStaffId,
    };

    public static PlatformStaffSignInResult Failure(SignInFailureReason reason, DateTime? lockoutEndDate = null) => new()
    {
        Succeeded = false,
        FailureReason = reason,
        LockoutEndDate = lockoutEndDate,
    };
}

/// <summary>
/// Verifies a platform staff email and password.
/// <para>
/// The near-duplicate of <see cref="Authentication.SignInService"/> that this class is, is
/// deliberate rather than an oversight. <c>PlatformStaffMember</c> is a different aggregate with no
/// shared base beyond <c>AggregateRoot</c>, so sharing this logic would mean either an interface
/// wide enough to blur what a tenant policy is (configurable) from what this is (fixed), or a shared
/// base type that would make <c>User</c> depend on something built for an account with no tenant.
/// Two short classes cost less than either.
/// </para>
/// </summary>
public sealed class PlatformStaffSignInService(
    IPlatformStaffRepository staff,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PlatformStaffSignInService> logger)
{
    /// <summary>
    /// The same dummy hash the tenant sign-in path verifies against for an unknown address, so the
    /// two "no such account" paths - tenant and platform - cost the same amount of time as each
    /// other and as a real, wrong-password check.
    /// </summary>
    private const string DummyHash =
        "AQAAAAIAAYagAAAAEJ8cJm7tJ8YVJ0Xh0Qp9mEYKQbZ5dQ8oQ3vQ2wJ0xY7yD5vX9kZ4nP2mR6sT8uW1g==";

    public async Task<PlatformStaffSignInResult> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        PlatformStaffMember? member = await staff.FindByEmailAsync(
            PlatformStaffMember.NormalizeEmailForLookup(email),
            cancellationToken);

        if (member is null)
        {
            passwordHasher.Verify(DummyHash, password);
            logger.LogInformation("Platform sign-in failed: no account matched the address supplied.");
            return PlatformStaffSignInResult.Failure(SignInFailureReason.InvalidCredentials);
        }

        if (member.IsLockedOut(now))
        {
            logger.LogWarning(
                "Platform sign-in refused for {PlatformStaffId}: locked out until {LockoutEnd}.",
                member.Id,
                member.LockoutEndDate);
            return PlatformStaffSignInResult.Failure(SignInFailureReason.LockedOut, member.LockoutEndDate);
        }

        if (member.Status == PlatformStaffStatus.Disabled)
        {
            logger.LogWarning("Platform sign-in refused for {PlatformStaffId}: the account is disabled.", member.Id);
            return PlatformStaffSignInResult.Failure(SignInFailureReason.AccountDisabled);
        }

        if (member.PasswordHash is null)
        {
            logger.LogInformation("Platform sign-in refused for {PlatformStaffId}: no password is set.", member.Id);
            return PlatformStaffSignInResult.Failure(SignInFailureReason.InvitationPending);
        }

        PasswordVerificationResult verification = passwordHasher.Verify(member.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            member.RecordFailedSignIn(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // No address in the log, as with tenant sign-ins: failed attempts are exactly where an
            // address would end up in a log aggregator more people can read than should.
            logger.LogWarning(
                "Platform sign-in failed for {PlatformStaffId}; {FailedCount} consecutive failures.",
                member.Id,
                member.AccessFailedCount);

            return member.IsLockedOut(now)
                ? PlatformStaffSignInResult.Failure(SignInFailureReason.LockedOut, member.LockoutEndDate)
                : PlatformStaffSignInResult.Failure(SignInFailureReason.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            member.SetPassword(passwordHasher.Hash(password), now);
            logger.LogInformation("Password for platform staff {PlatformStaffId} re-hashed with current parameters.", member.Id);
        }

        member.RecordSuccessfulSignIn(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlatformStaffSignInResult.Success(member.Id);
    }
}

/// <summary>Persistence for platform staff, kept as a port so sign-in stays testable without a database.</summary>
public interface IPlatformStaffRepository
{
    Task<PlatformStaffMember?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<PlatformStaffMember?> GetAsync(Guid platformStaffId, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(PlatformStaffMember member);
}
