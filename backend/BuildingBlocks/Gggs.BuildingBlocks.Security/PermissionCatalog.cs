using System.Collections.Frozen;
using System.Reflection;

namespace Gggs.BuildingBlocks.Security;

/// <summary>One permission as the platform defines it, for seeding and for the role editor UI.</summary>
public sealed record PermissionDefinition
{
    public required string Code { get; init; }

    public required string Module { get; init; }

    public required string Resource { get; init; }

    public required string Action { get; init; }

    /// <summary>
    /// Sensitive permissions expose personal or financial data. They require recent MFA (step-up)
    /// and every use is written to the sensitive-access audit trail.
    /// </summary>
    public bool IsSensitive { get; init; }

    /// <summary>Platform-only permissions are never offered to tenant roles.</summary>
    public bool IsPlatformOnly { get; init; }

    /// <summary>When set, the permission is hidden unless the tenant's plan includes this feature.</summary>
    public string? FeatureCode { get; init; }
}

/// <summary>
/// Reads the catalog out of <see cref="Permissions"/> by reflection, so the constants the endpoints
/// use and the rows seeded into the database can never drift apart.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>
    /// Codes that expose personal or financial data. Listed here rather than on each constant so
    /// that adding a sensitive permission is a deliberate, reviewable change.
    /// </summary>
    private static readonly FrozenSet<string> SensitiveCodes = new[]
    {
        Permissions.Employee.BankView,
        Permissions.Employee.BankManage,
        Permissions.Employee.IdentityDocumentView,
        Permissions.Employee.IdentityDocumentManage,
        Permissions.Employee.Export,
        Permissions.Audit.LogExport,
        Permissions.Audit.PrivacyRequestManage,
        Permissions.Payroll.EmployeeSalaryView,
        Permissions.Payroll.EmployeeSalaryManage,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<FrozenDictionary<string, PermissionDefinition>> Definitions = new(Build);

    /// <summary>Every permission the platform knows about.</summary>
    public static IReadOnlyCollection<PermissionDefinition> All => Definitions.Value.Values;

    public static bool Exists(string code) => Definitions.Value.ContainsKey(code);

    public static PermissionDefinition? Find(string code) => Definitions.Value.GetValueOrDefault(code);

    public static bool IsSensitive(string code) => Find(code)?.IsSensitive ?? false;

    private static FrozenDictionary<string, PermissionDefinition> Build()
    {
        var definitions = new Dictionary<string, PermissionDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (Type group in typeof(Permissions).GetNestedTypes(BindingFlags.Public))
        {
            bool isPlatformOnly = group.Name == nameof(Permissions.Platform);

            foreach (FieldInfo field in group.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                {
                    continue;
                }

                string code = (string)field.GetRawConstantValue()!;
                string[] parts = code.Split('.');
                if (parts.Length != 3)
                {
                    throw new InvalidOperationException(
                        $"Permission '{code}' ({group.Name}.{field.Name}) must be formatted as module.resource.action.");
                }

                var definition = new PermissionDefinition
                {
                    Code = code,
                    Module = parts[0],
                    Resource = parts[1],
                    Action = parts[2],
                    IsSensitive = SensitiveCodes.Contains(code),
                    IsPlatformOnly = isPlatformOnly,
                };

                if (!definitions.TryAdd(code, definition))
                {
                    throw new InvalidOperationException($"Permission code '{code}' is declared more than once.");
                }
            }
        }

        return definitions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
