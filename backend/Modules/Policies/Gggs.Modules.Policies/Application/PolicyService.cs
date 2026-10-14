using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Policies.Domain;
using Gggs.Modules.Policies.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Policies.Application;

public sealed record PolicyItem(
    Guid Id,
    string Code,
    string Title,
    string? Summary,
    PolicyCategory Category,
    bool RequiresAcknowledgement,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    string Audience,
    bool IsActive,
    int? PublishedVersion,
    DateOnly? EffectiveFrom,
    PolicyVersionStatus? OpenVersionStatus);

public sealed record PolicyVersionItem(
    Guid Id,
    int Number,
    string Content,
    string? ChangeSummary,
    DateOnly EffectiveFrom,
    PolicyVersionStatus Status,
    DateTime? PublishedDate,
    Guid? WorkflowInstanceId);

public sealed record PolicyDetail(PolicyItem Policy, IReadOnlyList<PolicyVersionItem> Versions);

public sealed record PublishResult(PolicyVersionStatus Status, Guid? WorkflowInstanceId);

public sealed record AcknowledgementRow(Guid EmployeeId, string? EmployeeName, string? EmployeeCode, DateTime? AcknowledgedDate);

public sealed record AcknowledgementReport(
    Guid PolicyVersionId,
    int VersionNumber,
    int AudienceCount,
    int AcknowledgedCount,
    IReadOnlyList<AcknowledgementRow> Rows);

public sealed record MyPolicyItem(
    Guid PolicyId,
    string Title,
    string? Summary,
    PolicyCategory Category,
    Guid VersionId,
    int VersionNumber,
    DateOnly EffectiveFrom,
    bool RequiresAcknowledgement,
    DateTime? AcknowledgedDate);

public sealed record MyPolicyDetail(MyPolicyItem Policy, string Content, string? ChangeSummary);

public sealed record PolicyRequest(
    string Code,
    string Title,
    string? Summary,
    PolicyCategory Category,
    bool RequiresAcknowledgement,
    IReadOnlyList<Guid>? AudienceDepartmentIds,
    IReadOnlyList<Guid>? AudienceLocationIds,
    string? Content,
    DateOnly? EffectiveFrom);

public sealed record PolicyVersionRequest(string Content, string? ChangeSummary, DateOnly EffectiveFrom);

/// <summary>
/// Policies (spec §110): writing them, publishing versions - through approval when the tenant has
/// defined one for <see cref="SubjectType"/> - and collecting acknowledgements.
/// </summary>
internal sealed class PolicyService(
    AppDbContext dbContext,
    IWorkflowStarter workflow,
    INotifier notifier,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const string SubjectType = "policy.publish";

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Publish a policy",
        "policies",
        [
            new WorkflowSubjectField("category", "Category", WorkflowFieldType.Text),
            new WorkflowSubjectField("requiresAcknowledgement", "Requires acknowledgement (true/false)", WorkflowFieldType.Text),
            new WorkflowSubjectField("audience", "Audience (everyone/targeted)", WorkflowFieldType.Text),
        ]);

    public async Task<IReadOnlyList<PolicyItem>> ListAsync(bool includeRetired, CancellationToken cancellationToken)
    {
        List<Policy> policies = await dbContext.Policies.AsNoTracking()
            .Where(policy => includeRetired || policy.IsActive)
            .OrderBy(policy => policy.Title)
            .ToListAsync(cancellationToken);

        return await ToItemsAsync(policies, cancellationToken);
    }

    public async Task<PolicyDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Policy policy = await FindAsync(id, track: false, cancellationToken);
        List<PolicyVersion> versions = await dbContext.PolicyVersions.AsNoTracking()
            .Where(version => version.PolicyId == id)
            .OrderByDescending(version => version.Number)
            .ToListAsync(cancellationToken);

        return new PolicyDetail((await ToItemsAsync([policy], cancellationToken))[0], [.. versions.Select(ToVersionItem)]);
    }

    public async Task<PolicyDetail> CreateAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string code = request.Code.Trim().ToUpperInvariant();

        if (await dbContext.Policies.AnyAsync(policy => policy.Code == code, cancellationToken))
        {
            throw new DomainRuleException("POLICY_CODE_IN_USE", $"A policy with code '{code}' already exists.");
        }

        (List<Guid> departments, List<Guid> locations) = await ValidateAudienceAsync(request.AudienceDepartmentIds, request.AudienceLocationIds, cancellationToken);
        var policy = Policy.Create(code, request.Title, request.Summary, request.Category, request.RequiresAcknowledgement, departments, locations);
        dbContext.Policies.Add(policy);

        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            dbContext.PolicyVersions.Add(PolicyVersion.Draft(
                policy.Id, 1, request.Content, "First version", request.EffectiveFrom ?? DateOnly.FromDateTime(Now)));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(policy.Id, cancellationToken);
    }

    public async Task<PolicyDetail> UpdateAsync(Guid id, PolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Policy policy = await FindAsync(id, track: true, cancellationToken);

        (List<Guid> departments, List<Guid> locations) = await ValidateAudienceAsync(request.AudienceDepartmentIds, request.AudienceLocationIds, cancellationToken);
        policy.Update(request.Title, request.Summary, request.Category, request.RequiresAcknowledgement, departments, locations);

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetRetiredAsync(Guid id, bool retired, CancellationToken cancellationToken)
    {
        Policy policy = await FindAsync(id, track: true, cancellationToken);
        if (retired)
        {
            policy.Retire();
        }
        else
        {
            policy.Reinstate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PolicyVersionItem> AddDraftAsync(Guid policyId, PolicyVersionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FindAsync(policyId, track: false, cancellationToken);

        if (await dbContext.PolicyVersions.AnyAsync(
                version => version.PolicyId == policyId && (version.Status == PolicyVersionStatus.Draft || version.Status == PolicyVersionStatus.PendingApproval),
                cancellationToken))
        {
            throw new DomainRuleException("POLICY_DRAFT_EXISTS", "This policy already has a version being worked on. Edit or publish it first.");
        }

        int next = (await dbContext.PolicyVersions.Where(version => version.PolicyId == policyId).MaxAsync(version => (int?)version.Number, cancellationToken) ?? 0) + 1;
        var draft = PolicyVersion.Draft(policyId, next, request.Content, request.ChangeSummary, request.EffectiveFrom);
        dbContext.PolicyVersions.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToVersionItem(draft);
    }

    public async Task<PolicyVersionItem> EditDraftAsync(Guid policyId, Guid versionId, PolicyVersionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PolicyVersion version = await FindVersionAsync(policyId, versionId, cancellationToken);
        version.Edit(request.Content, request.ChangeSummary, request.EffectiveFrom);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToVersionItem(version);
    }

    /// <summary>
    /// Publishes a draft, or sends it for approval when the tenant has a workflow for publishing
    /// policies. The draft is frozen either way; a refused approval makes it a draft again.
    /// </summary>
    public async Task<PublishResult> PublishAsync(Guid policyId, Guid versionId, CancellationToken cancellationToken)
    {
        Policy policy = await FindAsync(policyId, track: false, cancellationToken);
        if (!policy.IsActive)
        {
            throw new DomainRuleException("POLICY_RETIRED", "Reinstate the policy before publishing a version.");
        }

        PolicyVersion version = await FindVersionAsync(policyId, versionId, cancellationToken);
        if (version.Status != PolicyVersionStatus.Draft)
        {
            throw new DomainRuleException("POLICY_VERSION_STATE", $"Version {version.Number} is {version.Status}, not a draft.");
        }

        // A request sent back for changes is still open in Workflow; publishing the edited draft
        // replaces it with a fresh request rather than colliding with it. Saved on its own first, so
        // the new request never meets the old one in the open-request check or its unique index.
        await workflow.WithdrawAsync(SubjectType, version.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = version.Id,
                Title = $"Publish {policy.Title} v{version.Number}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = $"/policies/{policy.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["category"] = policy.Category.ToString(),
                    ["requiresAcknowledgement"] = policy.RequiresAcknowledgement ? "true" : "false",
                    ["audience"] = policy.AppliesToEveryone ? "everyone" : "targeted",
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            version.SubmitForApproval(started.InstanceId!.Value);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PublishResult(PolicyVersionStatus.PendingApproval, started.InstanceId);
        }

        await PublishNowAsync(policy, version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PublishResult(PolicyVersionStatus.Published, started.InstanceId);
    }

    /// <summary>Called when approval comes through.</summary>
    public async Task CompleteApprovedAsync(Guid versionId, CancellationToken cancellationToken)
    {
        PolicyVersion? version = await dbContext.PolicyVersions.FirstOrDefaultAsync(row => row.Id == versionId, cancellationToken);
        if (version is null || version.Status != PolicyVersionStatus.PendingApproval)
        {
            return;
        }

        Policy policy = await dbContext.Policies.AsNoTracking().FirstAsync(row => row.Id == version.PolicyId, cancellationToken);
        await PublishNowAsync(policy, version, cancellationToken);
    }

    /// <summary>Called when approval is refused, sent back or withdrawn.</summary>
    public async Task ReturnToDraftAsync(Guid versionId, CancellationToken cancellationToken)
    {
        PolicyVersion? version = await dbContext.PolicyVersions.FirstOrDefaultAsync(row => row.Id == versionId, cancellationToken);
        version?.ReturnToDraft();
    }

    public async Task<AcknowledgementReport> AcknowledgementsAsync(Guid policyId, Guid? versionId, CancellationToken cancellationToken)
    {
        Policy policy = await FindAsync(policyId, track: false, cancellationToken);

        PolicyVersion version = await dbContext.PolicyVersions.AsNoTracking()
            .Where(row => row.PolicyId == policyId && (versionId == null ? row.Status == PolicyVersionStatus.Published : row.Id == versionId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("POLICY_NOT_PUBLISHED", "This policy has no published version yet.");

        IReadOnlyList<Guid> audience = await AudienceAsync(policy, cancellationToken);
        Dictionary<Guid, DateTime> acknowledged = await dbContext.PolicyAcknowledgements.AsNoTracking()
            .Where(row => row.PolicyVersionId == version.Id)
            .ToDictionaryAsync(row => row.EmployeeId, row => row.AcknowledgedDate, cancellationToken);

        // Everyone who acknowledged stays in the report even if they have since left the audience.
        List<Guid> people = [.. audience.Union(acknowledged.Keys)];
        Dictionary<Guid, EmployeeSummary> summaries = (await employees.GetManyAsync(people, cancellationToken)).ToDictionary(person => person.Id);

        List<AcknowledgementRow> rows = [.. people
            .Select(id => new AcknowledgementRow(
                id,
                summaries.GetValueOrDefault(id)?.DisplayName,
                summaries.GetValueOrDefault(id)?.EmployeeCode,
                acknowledged.TryGetValue(id, out DateTime at) ? at : null))
            .OrderBy(row => row.AcknowledgedDate is not null)
            .ThenBy(row => row.EmployeeName, StringComparer.CurrentCulture)];

        return new AcknowledgementReport(version.Id, version.Number, audience.Count, acknowledged.Keys.Count(audience.Contains), rows);
    }

    // ----- Self-service -------------------------------------------------------------------------

    public async Task<IReadOnlyList<MyPolicyItem>> MyPoliciesAsync(CancellationToken cancellationToken)
    {
        (Guid me, EmployeeSummary person) = await MeAsync(cancellationToken);

        var published = await dbContext.PolicyVersions.AsNoTracking()
            .Where(version => version.Status == PolicyVersionStatus.Published)
            .Join(dbContext.Policies.AsNoTracking().Where(policy => policy.IsActive), version => version.PolicyId, policy => policy.Id, (version, policy) => new { version, policy })
            .ToListAsync(cancellationToken);

        List<Guid> versionIds = [.. published.Select(row => row.version.Id)];
        Dictionary<Guid, DateTime> mine = await dbContext.PolicyAcknowledgements.AsNoTracking()
            .Where(row => row.EmployeeId == me && versionIds.Contains(row.PolicyVersionId))
            .ToDictionaryAsync(row => row.PolicyVersionId, row => row.AcknowledgedDate, cancellationToken);

        return [.. published
            .Where(row => row.policy.Covers(person.DepartmentId, person.LocationId))
            .Select(row => ToMyItem(row.policy, row.version, mine.TryGetValue(row.version.Id, out DateTime at) ? at : null))
            .OrderBy(item => !item.RequiresAcknowledgement || item.AcknowledgedDate is not null)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)];
    }

    public async Task<MyPolicyDetail> MyPolicyAsync(Guid policyId, CancellationToken cancellationToken)
    {
        (Guid me, _, Policy policy, PolicyVersion version) = await MyPublishedAsync(policyId, cancellationToken);
        DateTime? acknowledged = await dbContext.PolicyAcknowledgements.AsNoTracking()
            .Where(row => row.PolicyVersionId == version.Id && row.EmployeeId == me)
            .Select(row => (DateTime?)row.AcknowledgedDate)
            .FirstOrDefaultAsync(cancellationToken);

        return new MyPolicyDetail(ToMyItem(policy, version, acknowledged), version.Content, version.ChangeSummary);
    }

    public async Task AcknowledgeAsync(Guid policyId, CancellationToken cancellationToken)
    {
        (Guid me, _, Policy policy, PolicyVersion version) = await MyPublishedAsync(policyId, cancellationToken);

        if (await dbContext.PolicyAcknowledgements.AnyAsync(row => row.PolicyVersionId == version.Id && row.EmployeeId == me, cancellationToken))
        {
            return;
        }

        dbContext.PolicyAcknowledgements.Add(PolicyAcknowledgement.Record(policy.Id, version.Id, me, Now, currentUser.IpAddress));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task PublishNowAsync(Policy policy, PolicyVersion version, CancellationToken cancellationToken)
    {
        List<PolicyVersion> current = await dbContext.PolicyVersions
            .Where(row => row.PolicyId == policy.Id && row.Status == PolicyVersionStatus.Published && row.Id != version.Id)
            .ToListAsync(cancellationToken);

        foreach (PolicyVersion previous in current)
        {
            previous.Supersede();
        }

        version.Publish(Now);

        if (policy.RequiresAcknowledgement)
        {
            IReadOnlyList<Guid> audience = await AudienceAsync(policy, cancellationToken);
            if (audience.Count > 0)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.PolicyPublished,
                        [.. audience.Select(id => new NotificationRecipient(id))],
                        new Dictionary<string, string>
                        {
                            ["policyTitle"] = policy.Title,
                            ["version"] = version.Number.ToString(CultureInfo.InvariantCulture),
                        },
                        $"/me/policies/{policy.Id}"),
                    cancellationToken);
            }
        }
    }

    private Task<IReadOnlyList<Guid>> AudienceAsync(Policy policy, CancellationToken cancellationToken)
        => policy.AppliesToEveryone
            ? employees.ListActiveIdsAsync(null, null, cancellationToken)
            : employees.ListActiveIdsAsync([.. policy.AudienceDepartmentIds], [.. policy.AudienceLocationIds], cancellationToken);

    private async Task<(List<Guid> Departments, List<Guid> Locations)> ValidateAudienceAsync(
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<Guid>? locationIds,
        CancellationToken cancellationToken)
    {
        List<Guid> departments = [.. (departmentIds ?? []).Distinct()];
        List<Guid> locations = [.. (locationIds ?? []).Distinct()];

        IReadOnlyList<OrgReference> invalid = await orgStructure.FindInvalidAsync(
            departments.Select(id => new OrgReference(OrgUnitKind.Department, id))
                .Concat(locations.Select(id => new OrgReference(OrgUnitKind.Location, id))),
            cancellationToken);

        if (invalid.Count > 0)
        {
            throw new DomainRuleException("POLICY_AUDIENCE_INVALID", "The audience names a department or location that does not exist or is inactive.");
        }

        return (departments, locations);
    }

    private async Task<(Guid Me, EmployeeSummary Person)> MeAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("POLICY_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        EmployeeSummary person = await employees.GetAsync(me, cancellationToken) ?? throw new NotFoundException("Employee", me);
        return (me, person);
    }

    private async Task<(Guid Me, EmployeeSummary Person, Policy Policy, PolicyVersion Version)> MyPublishedAsync(Guid policyId, CancellationToken cancellationToken)
    {
        (Guid me, EmployeeSummary person) = await MeAsync(cancellationToken);

        Policy policy = await dbContext.Policies.AsNoTracking().FirstOrDefaultAsync(row => row.Id == policyId && row.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(Policy), policyId);

        PolicyVersion version = await dbContext.PolicyVersions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.PolicyId == policyId && row.Status == PolicyVersionStatus.Published, cancellationToken)
            ?? throw new NotFoundException(nameof(Policy), policyId);

        // A policy that does not apply to me is reported as missing, like anything else out of reach.
        return policy.Covers(person.DepartmentId, person.LocationId) ? (me, person, policy, version) : throw new NotFoundException(nameof(Policy), policyId);
    }

    private async Task<Policy> FindAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        IQueryable<Policy> source = track ? dbContext.Policies : dbContext.Policies.AsNoTracking();
        return await source.FirstOrDefaultAsync(policy => policy.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Policy), id);
    }

    private async Task<PolicyVersion> FindVersionAsync(Guid policyId, Guid versionId, CancellationToken cancellationToken)
        => await dbContext.PolicyVersions.FirstOrDefaultAsync(version => version.Id == versionId && version.PolicyId == policyId, cancellationToken)
            ?? throw new NotFoundException(nameof(PolicyVersion), versionId);

    private async Task<List<PolicyItem>> ToItemsAsync(List<Policy> policies, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. policies.Select(policy => policy.Id)];
        var versions = await dbContext.PolicyVersions.AsNoTracking()
            .Where(version => ids.Contains(version.PolicyId) && version.Status != PolicyVersionStatus.Superseded)
            .Select(version => new { version.PolicyId, version.Number, version.Status, version.EffectiveFrom })
            .ToListAsync(cancellationToken);

        List<Guid> departmentIds = [.. policies.SelectMany(policy => policy.AudienceDepartmentIds).Distinct()];
        List<Guid> locationIds = [.. policies.SelectMany(policy => policy.AudienceLocationIds).Distinct()];
        IReadOnlyDictionary<Guid, string> departmentNames = await orgStructure.GetNamesAsync(OrgUnitKind.Department, departmentIds, cancellationToken);
        IReadOnlyDictionary<Guid, string> locationNames = await orgStructure.GetNamesAsync(OrgUnitKind.Location, locationIds, cancellationToken);

        return [.. policies.Select(policy =>
        {
            var published = versions.FirstOrDefault(version => version.PolicyId == policy.Id && version.Status == PolicyVersionStatus.Published);
            var open = versions.FirstOrDefault(version => version.PolicyId == policy.Id && version.Status is PolicyVersionStatus.Draft or PolicyVersionStatus.PendingApproval);

            string audience = policy.AppliesToEveryone
                ? "Everyone"
                : string.Join(", ", policy.AudienceDepartmentIds.Select(id => departmentNames.GetValueOrDefault(id) ?? "?")
                    .Concat(policy.AudienceLocationIds.Select(id => locationNames.GetValueOrDefault(id) ?? "?")));

            return new PolicyItem(
                policy.Id,
                policy.Code,
                policy.Title,
                policy.Summary,
                policy.Category,
                policy.RequiresAcknowledgement,
                policy.AudienceDepartmentIds,
                policy.AudienceLocationIds,
                audience,
                policy.IsActive,
                published?.Number,
                published?.EffectiveFrom,
                open?.Status);
        })];
    }

    private static PolicyVersionItem ToVersionItem(PolicyVersion version)
        => new(version.Id, version.Number, version.Content, version.ChangeSummary, version.EffectiveFrom, version.Status, version.PublishedDate, version.WorkflowInstanceId);

    private static MyPolicyItem ToMyItem(Policy policy, PolicyVersion version, DateTime? acknowledged)
        => new(policy.Id, policy.Title, policy.Summary, policy.Category, version.Id, version.Number, version.EffectiveFrom, policy.RequiresAcknowledgement, acknowledged);
}

/// <summary>Publishes a version once its approval completes.</summary>
internal sealed class PublishPolicyOnApproval(PolicyService policies) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == PolicyService.SubjectType)
        {
            await policies.CompleteApprovedAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnPolicyOnRejection(PolicyService policies) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == PolicyService.SubjectType)
        {
            await policies.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnPolicyOnSendBack(PolicyService policies) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == PolicyService.SubjectType)
        {
            await policies.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnPolicyOnWithdrawal(PolicyService policies) : IIntegrationEventConsumer<WorkflowWithdrawnIntegrationEvent>
{
    public async Task HandleAsync(WorkflowWithdrawnIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == PolicyService.SubjectType)
        {
            await policies.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}
