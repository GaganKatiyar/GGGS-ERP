using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Identity.Domain.PlatformStaff.Events;

namespace Gggs.Modules.Identity.Domain.PlatformStaff;

public enum PlatformStaffStatus
{
    /// <summary>Invited but has not yet set a password or signed in.</summary>
    Invited = 1,

    Active = 2,

    /// <summary>Locked by repeated failed sign-ins. Temporary and self-clearing.</summary>
    Locked = 3,

    /// <summary>Switched off on departure. Deliberate and lasting.</summary>
    Disabled = 4,
}

/// <summary>
/// An account belonging to our own staff, not to any customer.
/// <para>
/// Deliberately not a <see cref="Users.User"/>. A tenant user is owned by exactly one tenant and is
/// meaningless without one; platform staff are owned by none, and reusing the tenant-owned type for
/// them would mean either inventing a fake tenant to hold their rows - which would put platform
/// operations inside a customer's isolation boundary, the one thing every layer of §9 exists to
/// prevent - or teaching the tenant interceptor to accept a row with no tenant, which would turn an
/// invariant the isolation tests currently rely on (a tenant-owned row's TenantId is never empty)
/// into a maybe. A second, small aggregate costs less than either.
/// </para>
/// <para>
/// The account rules mirror <see cref="Users.User"/> closely - invitation, lockout, password
/// history - because the security properties a credential needs do not depend on who holds it.
/// What differs is smaller and fixed rather than configurable: platform staff have no tenant to own
/// a <see cref="Users.PasswordPolicy"/>, so the rules here are constants, chosen to be at least as
/// strict as a tenant's own defaults - a compromised platform account reaches every tenant's
/// commercial data, not one customer's.
/// </para>
/// </summary>
public sealed class PlatformStaffMember : AggregateRoot, IPlatformOwned, IAuditable, IConcurrencyAware
{
    /// <summary>
    /// Fourteen, not the tenant default of twelve. The extra margin is cheap and this account is a
    /// higher-value target than any single tenant's.
    /// </summary>
    private const int MinimumPasswordLength = 14;

    private const int PasswordHistoryCount = 10;

    private const int LockoutThreshold = 5;

    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<PasswordHistoryEntry> _passwordHistory = [];

    private PlatformStaffMember()
    {
        Email = null!;
        NormalizedEmail = null!;
        DisplayName = null!;
        SecurityStamp = null!;
    }

    public string Email { get; private set; }

    /// <summary>Upper-cased for lookup, so comparisons never depend on collation.</summary>
    public string NormalizedEmail { get; private set; }

    public string DisplayName { get; private set; }

    public PlatformStaffStatus Status { get; private set; }

    /// <summary>Null for an invited member who has not set one yet. Its absence is meaningful, not an error.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Rotated whenever credentials or roles change. Tokens carry no permissions but do carry a
    /// session bound to this stamp indirectly through <see cref="PermissionVersion"/>'s sibling
    /// mechanism - rotating it is what "sign out everywhere" means for this account.
    /// </summary>
    public string SecurityStamp { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTime? LockoutEndDate { get; private set; }

    public DateTime? PasswordChangedDate { get; private set; }

    public DateTime? LastLoginDate { get; private set; }

    /// <summary>
    /// Bumped whenever role grants change. Tokens carry it and the permission cache is keyed by it,
    /// so a revoked role takes effect on the next request rather than when the token expires.
    /// </summary>
    public int PermissionVersion { get; private set; } = 1;

    public string? InvitationTokenHash { get; private set; }

    public DateTime? InvitationTokenExpiresDate { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public byte[]? RowVersion { get; private set; }

    public IReadOnlyCollection<PasswordHistoryEntry> PasswordHistory => _passwordHistory;

    public bool CanSignIn => Status is PlatformStaffStatus.Active or PlatformStaffStatus.Invited;

    public static PlatformStaffMember Invite(string email, string displayName, DateTime now)
    {
        var member = new PlatformStaffMember
        {
            Email = NormalizeEmailForStorage(email),
            NormalizedEmail = NormalizeEmailForLookup(email),
            DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName).Trim(), 200),
            Status = PlatformStaffStatus.Invited,
            SecurityStamp = NewSecurityStamp(),
        };

        member.Raise(new PlatformStaffInvitedDomainEvent(member.Id, member.Email, member.DisplayName));
        return member;
    }

    public void SetInvitationToken(string tokenHash, DateTime expiresDate)
    {
        InvitationTokenHash = Guard.NotNullOrWhiteSpace(tokenHash);
        InvitationTokenExpiresDate = expiresDate;
    }

    public bool IsInvitationTokenValid(string tokenHash, DateTime now)
        => InvitationTokenHash is not null
            && InvitationTokenExpiresDate is not null
            && InvitationTokenExpiresDate > now
            && string.Equals(InvitationTokenHash, tokenHash, StringComparison.Ordinal);

    public void CompleteInvitation(string passwordHash, DateTime now)
    {
        Guard.Against(
            Status != PlatformStaffStatus.Invited,
            "PLATFORM_STAFF_NOT_INVITED",
            $"Only an invited member can complete an invitation; this one is {Status}.");

        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash);
        PasswordChangedDate = now;
        Status = PlatformStaffStatus.Active;
        RotateSecurityStamp();
        ClearInvitationToken();

        _passwordHistory.Add(new PasswordHistoryEntry(passwordHash, now));
    }

    private void ClearInvitationToken()
    {
        InvitationTokenHash = null;
        InvitationTokenExpiresDate = null;
    }

    public void RecordSuccessfulSignIn(DateTime now)
    {
        Guard.Against(!CanSignIn, "PLATFORM_STAFF_CANNOT_SIGN_IN", $"A {Status} member cannot sign in.");

        AccessFailedCount = 0;
        LockoutEndDate = null;
        LastLoginDate = now;

        if (Status == PlatformStaffStatus.Locked)
        {
            Status = PlatformStaffStatus.Active;
        }

        Raise(new PlatformStaffSignedInDomainEvent(Id, Email));
    }

    public void RecordFailedSignIn(DateTime now)
    {
        AccessFailedCount++;

        if (AccessFailedCount >= LockoutThreshold)
        {
            Status = PlatformStaffStatus.Locked;
            LockoutEndDate = now.Add(LockoutDuration);
            Raise(new PlatformStaffLockedOutDomainEvent(Id, Email, LockoutEndDate.Value));
        }
    }

    public bool IsLockedOut(DateTime now)
        => Status == PlatformStaffStatus.Locked && LockoutEndDate is not null && LockoutEndDate > now;

    public void Unlock()
    {
        Guard.Against(Status != PlatformStaffStatus.Locked, "PLATFORM_STAFF_NOT_LOCKED", "The member is not locked.");

        Status = PlatformStaffStatus.Active;
        AccessFailedCount = 0;
        LockoutEndDate = null;
    }

    /// <summary>
    /// Checks a candidate password against the fixed rules.
    /// <para>
    /// No tenant to hold a configurable policy, so this is the whole check: length, and not the
    /// account holder's own name or address. Breach checking and reuse are the caller's job, which
    /// has the corpus and this account's history.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Validate(string password, string? displayName, string? email)
    {
        List<string> failures = [];

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            failures.Add($"Password must be at least {MinimumPasswordLength} characters.");
        }

        if (password?.Length > 256)
        {
            failures.Add("Password must be no more than 256 characters.");
        }

        if (password is not null && ContainsPersonalInformation(password, displayName, email))
        {
            failures.Add("Password must not contain your name or email address.");
        }

        return failures;
    }

    public void SetPassword(string passwordHash, DateTime now)
    {
        Guard.NotNullOrWhiteSpace(passwordHash);

        Guard.Against(
            Status == PlatformStaffStatus.Disabled,
            "PLATFORM_STAFF_DISABLED",
            "A disabled member's password cannot be changed; re-enable the account first.");

        PasswordHash = passwordHash;
        PasswordChangedDate = now;
        RotateSecurityStamp();
        ClearInvitationToken();

        _passwordHistory.Add(new PasswordHistoryEntry(passwordHash, now));
        TrimPasswordHistory();

        if (Status == PlatformStaffStatus.Locked)
        {
            Status = PlatformStaffStatus.Active;
            AccessFailedCount = 0;
            LockoutEndDate = null;
        }

        Raise(new PlatformStaffPasswordChangedDomainEvent(Id, Email));
    }

    public IReadOnlyList<string> RecentPasswordHashes()
        => [.. _passwordHistory.OrderByDescending(entry => entry.CreatedDate).Take(PasswordHistoryCount).Select(entry => entry.PasswordHash)];

    public void Disable()
    {
        Guard.Against(Status == PlatformStaffStatus.Disabled, "PLATFORM_STAFF_ALREADY_DISABLED", "The member is already disabled.");

        Status = PlatformStaffStatus.Disabled;

        // Ends every existing session immediately, rather than letting someone who just left keep
        // working until their token expires.
        RotateSecurityStamp();
        Raise(new PlatformStaffDisabledDomainEvent(Id, Email));
    }

    public void BumpPermissionVersion() => PermissionVersion++;

    private void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    private void TrimPasswordHistory()
    {
        List<PasswordHistoryEntry> excess =
        [
            .. _passwordHistory.OrderByDescending(entry => entry.CreatedDate).Skip(PasswordHistoryCount)
        ];

        foreach (PasswordHistoryEntry entry in excess)
        {
            _passwordHistory.Remove(entry);
        }
    }

    private static string NewSecurityStamp() => Guid.CreateVersion7().ToString("N");

    private static string NormalizeEmailForStorage(string email)
    {
        string trimmed = Guard.NotNullOrWhiteSpace(email).Trim();

        Guard.Against(
            !trimmed.Contains('@', StringComparison.Ordinal) || trimmed.Length > 256,
            "PLATFORM_STAFF_EMAIL_INVALID",
            "A valid email address is required.");

        return trimmed;
    }

    public static string NormalizeEmailForLookup(string email)
        => NormalizeEmailForStorage(email).ToUpperInvariant();

    private static bool ContainsPersonalInformation(string password, string? displayName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            foreach (string part in displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 4 && password.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            string localPart = email.Split('@')[0];
            if (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A previous password hash, kept so reuse can be refused.</summary>
public sealed record PasswordHistoryEntry(string PasswordHash, DateTime CreatedDate);
