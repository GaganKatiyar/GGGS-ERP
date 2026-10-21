namespace Gggs.Modules.Identity.Contracts;

/// <summary>The sign-in account linked to an employee, as other modules need to see it.</summary>
public sealed record UserAccountInfo(Guid UserId, string Email, string Status);

/// <summary>
/// Account deprovisioning for leavers (Separation). Disabling ends every session at once (the
/// security stamp rotates). Writes into the caller's unit of work and saves nothing itself.
/// </summary>
public interface IUserAccounts
{
    Task<UserAccountInfo?> FindByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Disables every enabled account linked to the employee; returns how many were disabled.</summary>
    Task<int> DisableForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
