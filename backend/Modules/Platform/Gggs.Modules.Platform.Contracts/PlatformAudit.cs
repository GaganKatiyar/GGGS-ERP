namespace Gggs.Modules.Platform.Contracts;

/// <summary>
/// The platform console's audit trail: what platform staff did, to which organisation, and why.
/// <para>
/// Recording adds the entry to the caller's unit of work, so it commits with the change it describes
/// or not at all - record first, then save. Only the console server registers an implementation.
/// </para>
/// </summary>
public interface IPlatformAuditLog
{
    void Record(string action, string summary, Guid? tenantId = null, string? tenantCode = null, string? reason = null);
}
