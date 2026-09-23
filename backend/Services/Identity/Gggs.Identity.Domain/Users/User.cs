using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Identity.Domain.Users.Events;

namespace Gggs.Identity.Domain.Users;

/// <summary>What kind of account this is. Decides which boundary the user lives inside.</summary>
public enum UserType
{
    /// <summary>Belongs to one customer. The overwhelming majority of accounts.</summary>
    TenantUser = 1,

    /// <summary>Our own staff. Belongs to no tenant and cannot hold tenant permissions.</summary>
    PlatformUser = 2,

    /// <summary>A machine account for the public API. Authenticates with client credentials.</summary>
    ApiClient = 3,
}

public enum UserStatus
{
    /// <summary>Invited but has not yet set a password or signed in.</summary>
    Invited = 1,

    Active = 2,

    /// <summary>Locked by repeated failed sign-ins. Temporary and self-clearing.</summary>
    Locked = 3,

    /// <summary>Switched off by an administrator, or on leaving. Deliberate and lasting.</summary>
    Disabled = 4,
}

/// <summary>
/// How a user proves who they are.
/// <para>
/// Modelled now although only <see cref="LocalPassword"/> is implemented, because the choice is
/// recorded per user and per session. Adding federation later means populating it, not migrating
/// every account to a new shape.
/// </para>
/// </summary>
public enum AuthenticationMethod
{
    LocalPassword = 1,

    /// <summary>Entra ID, Okta, Google or any OIDC provider the tenant configures.</summary>
    ExternalIdentityProvider = 2,
}

/// <summary>
/// An account that can sign in.
/// <para>
/// A user is not an employee. Most users are employees, but platform staff, integration accounts
/// and contractors are not, and an employee with no system access has no user at all. Keeping them
/// separate is what lets an employee be offboarded while their records live on.
/// </para>
/// <para>
/// The password is deliberately optional. A federated user has none, and building that in now -
/// rather than assuming every account has a password - is what keeps Entra ID federation an
/// addition rather than a migration.
/// </para>
/// </summary>
public sealed class User : TenantAggregateRoot
{
    private readonly List<ExternalLogin> _externalLogins = [];
    private readonly List<PasswordHistoryEntry> _passwordHistory = [];

    private User()
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

    public UserType UserType { get; private set; }

    public UserStatus Status { get; private set; }

    /// <summary>
    /// Null for a federated user, and for an invited user who has not set one yet. Its absence is
    /// meaningful rather than an error state.
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Rotated whenever credentials or roles change. Tokens carry it, so rotating it makes every
    /// existing session invalid - the mechanism behind "sign out everywhere" and behind a password
    /// reset not leaving an attacker signed in.
    /// </summary>
    public string SecurityStamp { get; private set; }

    /// <summary>The employee this account belongs to, when it belongs to one.</summary>
    public Guid? EmployeeId { get; private set; }

    public int AccessFailedCount { get; private set; }

    /// <summary>When the lockout expires. Lockouts are timed, not permanent.</summary>
    public DateTime? LockoutEndDate { get; private set; }

    public DateTime? PasswordChangedDate { get; private set; }

    public DateTime? LastLoginDate { get; private set; }

    public bool MfaEnabled { get; private set; }

    /// <summary>
    /// Bumped whenever roles change. Tokens carry it and the permission cache is keyed by it, so a
    /// revoked role takes effect on the next request instead of when the token expires.
    /// </summary>
    public int PermissionVersion { get; private set; } = 1;

    public string? PreferredLocale { get; private set; }

    public string? TimeZone { get; private set; }

    /// <summary>Federated identities linked to this account. Empty until federation is enabled.</summary>
    public IReadOnlyCollection<ExternalLogin> ExternalLogins => _externalLogins;

    /// <summary>Previous password hashes, so a policy can refuse reuse.</summary>
    public IReadOnlyCollection<PasswordHistoryEntry> PasswordHistory => _passwordHistory;

    /// <summary>Whether this account can sign in at all right now.</summary>
    public bool CanSignIn => Status is UserStatus.Active or UserStatus.Invited;

    /// <summary>
    /// Invites a user. No password is set here: an administrator typing one and emailing it to
    /// someone means the administrator knows it, and it usually travels in plain text.
    /// </summary>
    public static User Invite(
        string email,
        string displayName,
        UserType userType,
        Guid? employeeId,
        DateTime now)
    {
        var user = new User
        {
            Email = NormalizeEmailForStorage(email),
            NormalizedEmail = NormalizeEmailForLookup(email),
            DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName).Trim(), 200),
            UserType = userType,
            EmployeeId = employeeId,
            Status = UserStatus.Invited,
            SecurityStamp = NewSecurityStamp(),
        };

        user.Raise(new UserInvitedDomainEvent(user.Id, user.Email, user.DisplayName));
        return user;
    }

    /// <summary>
    /// Completes an invitation by setting the first password. The caller has already checked the
    /// invitation token; this only enforces the account rules.
    /// </summary>
    public void CompleteInvitation(string passwordHash, DateTime now)
    {
        Guard.Against(
            Status != UserStatus.Invited,
            "USER_NOT_INVITED",
            $"Only an invited user can complete an invitation; this one is {Status}.");

        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash);
        PasswordChangedDate = now;
        Status = UserStatus.Active;
        RotateSecurityStamp();

        _passwordHistory.Add(new PasswordHistoryEntry(passwordHash, now));
    }

    /// <summary>
    /// Records a successful sign-in and clears any accumulated failures.
    /// <para>
    /// Clearing matters: without it, occasional typos spread over months would eventually lock out
    /// someone who has been signing in successfully the whole time.
    /// </para>
    /// </summary>
    public void RecordSuccessfulSignIn(DateTime now)
    {
        Guard.Against(!CanSignIn, "USER_CANNOT_SIGN_IN", $"A {Status} user cannot sign in.");

        AccessFailedCount = 0;
        LockoutEndDate = null;
        LastLoginDate = now;

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }

        Raise(new UserSignedInDomainEvent(Id, Email));
    }

    /// <summary>
    /// Records a failed attempt and locks the account once the threshold is reached.
    /// <para>
    /// The lockout is timed rather than permanent, because a permanent one turns a forgotten
    /// password into a support ticket and hands anyone who knows an email address a way to lock
    /// that person out at will.
    /// </para>
    /// </summary>
    public void RecordFailedSignIn(PasswordPolicy policy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        AccessFailedCount++;

        if (AccessFailedCount >= policy.LockoutThreshold)
        {
            Status = UserStatus.Locked;
            LockoutEndDate = now.Add(policy.LockoutDuration);
            Raise(new UserLockedOutDomainEvent(Id, Email, LockoutEndDate.Value));
        }
    }

    /// <summary>Whether a timed lockout is still in force.</summary>
    public bool IsLockedOut(DateTime now)
        => Status == UserStatus.Locked && LockoutEndDate is not null && LockoutEndDate > now;

    /// <summary>Clears a lockout early, at an administrator's request.</summary>
    public void Unlock()
    {
        Guard.Against(Status != UserStatus.Locked, "USER_NOT_LOCKED", "The user is not locked.");

        Status = UserStatus.Active;
        AccessFailedCount = 0;
        LockoutEndDate = null;
    }

    /// <summary>
    /// Changes the password. Rotating the security stamp is the point: whoever prompted the change
    /// - the user, or an administrator after a compromise - expects existing sessions to end.
    /// </summary>
    public void SetPassword(string passwordHash, PasswordPolicy policy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Guard.NotNullOrWhiteSpace(passwordHash);

        Guard.Against(
            Status == UserStatus.Disabled,
            "USER_DISABLED",
            "A disabled user's password cannot be changed; re-enable the account first.");

        PasswordHash = passwordHash;
        PasswordChangedDate = now;
        RotateSecurityStamp();

        _passwordHistory.Add(new PasswordHistoryEntry(passwordHash, now));
        TrimPasswordHistory(policy.HistoryCount);

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
            AccessFailedCount = 0;
            LockoutEndDate = null;
        }

        Raise(new UserPasswordChangedDomainEvent(Id, Email));
    }

    /// <summary>Hashes a policy would refuse to reuse, newest first.</summary>
    public IReadOnlyList<string> RecentPasswordHashes(int count)
        => [.. _passwordHistory.OrderByDescending(entry => entry.CreatedDate).Take(count).Select(entry => entry.PasswordHash)];

    public void Disable()
    {
        Guard.Against(Status == UserStatus.Disabled, "USER_ALREADY_DISABLED", "The user is already disabled.");

        Status = UserStatus.Disabled;

        // Ends every existing session immediately, rather than letting a departing employee keep
        // working until their token expires.
        RotateSecurityStamp();
        Raise(new UserDisabledDomainEvent(Id, Email));
    }

    public void Enable()
    {
        Guard.Against(Status != UserStatus.Disabled, "USER_NOT_DISABLED", "The user is not disabled.");

        // Back to Invited when they never set a password, so they get an invitation rather than a
        // sign-in page they cannot use.
        Status = PasswordHash is null ? UserStatus.Invited : UserStatus.Active;
        AccessFailedCount = 0;
        LockoutEndDate = null;
    }

    public void SetMfaEnabled(bool enabled)
    {
        MfaEnabled = enabled;
        RotateSecurityStamp();
    }

    public void LinkToEmployee(Guid employeeId) => EmployeeId = Guard.NotEmpty(employeeId);

    public void UpdateProfile(string displayName, string? preferredLocale, string? timeZone)
    {
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName).Trim(), 200);
        PreferredLocale = preferredLocale;
        TimeZone = timeZone;
    }

    /// <summary>
    /// Records that roles changed, so cached permissions are rebuilt on the next request rather
    /// than when the token expires.
    /// </summary>
    public void BumpPermissionVersion() => PermissionVersion++;

    /// <summary>
    /// Links a federated identity.
    /// <para>
    /// Present now, unused until Entra ID federation ships. It exists so a user can gain a
    /// federated identity later without losing their account, their roles or their history - which
    /// is what would happen if federated users had to be created as separate accounts.
    /// </para>
    /// </summary>
    public void LinkExternalLogin(string provider, string providerKey, string? displayName, DateTime now)
    {
        Guard.NotNullOrWhiteSpace(provider);
        Guard.NotNullOrWhiteSpace(providerKey);

        bool alreadyLinked = _externalLogins.Any(login =>
            string.Equals(login.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(login.ProviderKey, providerKey, StringComparison.Ordinal));

        if (alreadyLinked)
        {
            return;
        }

        _externalLogins.Add(new ExternalLogin(provider, providerKey, displayName, now));
    }

    public void UnlinkExternalLogin(string provider, string providerKey)
    {
        _externalLogins.RemoveAll(login =>
            string.Equals(login.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(login.ProviderKey, providerKey, StringComparison.Ordinal));

        Guard.Against(
            PasswordHash is null && _externalLogins.Count == 0 && Status != UserStatus.Disabled,
            "USER_WOULD_HAVE_NO_CREDENTIAL",
            "Removing that login would leave the account with no way to sign in.");
    }

    private void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    private void TrimPasswordHistory(int keep)
    {
        if (keep <= 0)
        {
            _passwordHistory.Clear();
            return;
        }

        List<PasswordHistoryEntry> excess =
        [
            .. _passwordHistory.OrderByDescending(entry => entry.CreatedDate).Skip(keep)
        ];

        foreach (PasswordHistoryEntry entry in excess)
        {
            _passwordHistory.Remove(entry);
        }
    }

    private static string NewSecurityStamp() => Guid.CreateVersion7().ToString("N");

    /// <summary>Trimmed but case-preserved, because people expect their own capitalisation back.</summary>
    private static string NormalizeEmailForStorage(string email)
    {
        string trimmed = Guard.NotNullOrWhiteSpace(email).Trim();

        Guard.Against(
            !trimmed.Contains('@', StringComparison.Ordinal) || trimmed.Length > 256,
            "USER_EMAIL_INVALID",
            "A valid email address is required.");

        return trimmed;
    }

    /// <summary>Upper-cased for lookup and uniqueness, independent of database collation.</summary>
    public static string NormalizeEmailForLookup(string email)
        => NormalizeEmailForStorage(email).ToUpperInvariant();
}

/// <summary>A federated identity linked to an account.</summary>
public sealed record ExternalLogin(string Provider, string ProviderKey, string? DisplayName, DateTime LinkedDate);

/// <summary>A previous password hash, kept so a policy can refuse reuse.</summary>
public sealed record PasswordHistoryEntry(string PasswordHash, DateTime CreatedDate);
