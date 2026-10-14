namespace Gggs.Modules.MasterData.Contracts;

/// <summary>
/// The lookup lists every module validates against, by code.
/// <para>
/// Stable names, because other modules store these codes in their own rows (an employee's
/// employment type, a client's country). Add freely; never rename.
/// </para>
/// </summary>
public static class LookupLists
{
    public const string Country = "country";
    public const string State = "state";
    public const string Currency = "currency";
    public const string EmploymentType = "employment-type";
    public const string WorkMode = "work-mode";
    public const string Gender = "gender";
    public const string MaritalStatus = "marital-status";
    public const string BloodGroup = "blood-group";
    public const string Relationship = "relationship";
    public const string ExitReason = "exit-reason";
    public const string JobFamily = "job-family";
    public const string Industry = "industry";
}

/// <summary>
/// In-process lookup validation for other modules (ADR-12): "is this a code the tenant uses?"
/// without reading another module's tables.
/// </summary>
public interface IMasterDataLookup
{
    /// <summary>True when <paramref name="code"/> is an active value of <paramref name="listCode"/> for the current tenant.</summary>
    Task<bool> IsValidAsync(string listCode, string code, CancellationToken cancellationToken = default);

    /// <summary>Labels for the given codes; codes the tenant does not use are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string listCode,
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default);
}
