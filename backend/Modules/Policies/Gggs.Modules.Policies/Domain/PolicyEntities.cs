using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Policies.Domain;

public enum PolicyCategory
{
    General = 1,
    Conduct = 2,
    Leave = 3,
    Attendance = 4,
    Compensation = 5,
    Travel = 6,
    InformationSecurity = 7,
    HealthAndSafety = 8,
    Other = 9,
}

/// <summary>
/// A company policy (spec §110): what it is, who it applies to, and whether people must confirm
/// they have read it. Its words live in versions; at most one version is in effect at a time.
/// </summary>
public sealed class Policy : TenantAggregateRoot
{
    private Policy()
    {
        Code = null!;
        Title = null!;
        AudienceDepartmentIdsJson = "[]";
        AudienceLocationIdsJson = "[]";
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Summary { get; private set; }

    public PolicyCategory Category { get; private set; }

    public bool RequiresAcknowledgement { get; private set; }

    /// <summary>Departments it applies to. Empty, with no locations either, means everyone.</summary>
    public string AudienceDepartmentIdsJson { get; private set; }

    public string AudienceLocationIdsJson { get; private set; }

    public IReadOnlyList<Guid> AudienceDepartmentIds => JsonSerializer.Deserialize<List<Guid>>(AudienceDepartmentIdsJson) ?? [];

    public IReadOnlyList<Guid> AudienceLocationIds => JsonSerializer.Deserialize<List<Guid>>(AudienceLocationIdsJson) ?? [];

    public bool AppliesToEveryone => AudienceDepartmentIds.Count == 0 && AudienceLocationIds.Count == 0;

    public static Policy Create(
        string code,
        string title,
        string? summary,
        PolicyCategory category,
        bool requiresAcknowledgement,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds)
    {
        var policy = new Policy { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32) };
        policy.Update(title, summary, category, requiresAcknowledgement, departmentIds, locationIds);
        return policy;
    }

    public void Update(
        string title,
        string? summary,
        PolicyCategory category,
        bool requiresAcknowledgement,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds)
    {
        ArgumentNullException.ThrowIfNull(departmentIds);
        ArgumentNullException.ThrowIfNull(locationIds);

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Summary = string.IsNullOrWhiteSpace(summary) ? null : Guard.MaxLength(summary.Trim(), 1000);
        Category = Enum.IsDefined(category) ? category : throw new DomainRuleException("POLICY_CATEGORY", "Unknown policy category.");
        RequiresAcknowledgement = requiresAcknowledgement;
        AudienceDepartmentIdsJson = JsonSerializer.Serialize(departmentIds.Distinct().ToList());
        AudienceLocationIdsJson = JsonSerializer.Serialize(locationIds.Distinct().ToList());
    }

    /// <summary>Whether someone sitting in this department and location is covered.</summary>
    public bool Covers(Guid? departmentId, Guid? locationId)
        => AppliesToEveryone
            || (departmentId is { } department && AudienceDepartmentIds.Contains(department))
            || (locationId is { } location && AudienceLocationIds.Contains(location));

    public void Retire() => IsActive = false;

    public void Reinstate() => IsActive = true;
}

public enum PolicyVersionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Published = 3,
    Superseded = 4,
}

/// <summary>One wording of a policy. Drafts are edited freely; a published version is frozen.</summary>
public sealed class PolicyVersion : TenantAggregateRoot
{
    private PolicyVersion()
    {
        Content = null!;
    }

    public Guid PolicyId { get; private set; }

    public int Number { get; private set; }

    /// <summary>The policy text, as Markdown.</summary>
    public string Content { get; private set; }

    public string? ChangeSummary { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public PolicyVersionStatus Status { get; private set; }

    public DateTime? PublishedDate { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public static PolicyVersion Draft(Guid policyId, int number, string content, string? changeSummary, DateOnly effectiveFrom)
    {
        var version = new PolicyVersion { PolicyId = policyId, Number = number, Status = PolicyVersionStatus.Draft };
        version.Edit(content, changeSummary, effectiveFrom);
        return version;
    }

    public void Edit(string content, string? changeSummary, DateOnly effectiveFrom)
    {
        EnsureDraft();
        Content = Guard.MaxLength(Guard.NotNullOrWhiteSpace(content), 200_000);
        ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : Guard.MaxLength(changeSummary.Trim(), 500);
        EffectiveFrom = effectiveFrom;
    }

    public void SubmitForApproval(Guid workflowInstanceId)
    {
        EnsureDraft();
        Status = PolicyVersionStatus.PendingApproval;
        WorkflowInstanceId = workflowInstanceId;
    }

    public void Publish(DateTime now)
    {
        if (Status is not (PolicyVersionStatus.Draft or PolicyVersionStatus.PendingApproval))
        {
            throw new DomainRuleException("POLICY_VERSION_STATE", $"Version {Number} is {Status} and cannot be published.");
        }

        Status = PolicyVersionStatus.Published;
        PublishedDate = now;
    }

    /// <summary>Approval was refused, sent back or withdrawn: the version is a draft again.</summary>
    public void ReturnToDraft()
    {
        if (Status == PolicyVersionStatus.PendingApproval)
        {
            Status = PolicyVersionStatus.Draft;
            WorkflowInstanceId = null;
        }
    }

    public void Supersede() => Status = PolicyVersionStatus.Superseded;

    private void EnsureDraft()
    {
        if (Status != PolicyVersionStatus.Draft)
        {
            throw new DomainRuleException("POLICY_VERSION_FROZEN", $"Version {Number} is {Status}; only drafts can change.");
        }
    }
}

/// <summary>"I have read and understood version N." Recorded once per person per version.</summary>
public sealed class PolicyAcknowledgement : TenantAggregateRoot
{
    private PolicyAcknowledgement()
    {
    }

    public Guid PolicyId { get; private set; }

    public Guid PolicyVersionId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateTime AcknowledgedDate { get; private set; }

    public string? IpAddress { get; private set; }

    public static PolicyAcknowledgement Record(Guid policyId, Guid versionId, Guid employeeId, DateTime now, string? ipAddress) => new()
    {
        PolicyId = policyId,
        PolicyVersionId = versionId,
        EmployeeId = Guard.NotEmpty(employeeId),
        AcknowledgedDate = now,
        IpAddress = ipAddress is null ? null : Guard.MaxLength(ipAddress, 64),
    };
}
