using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Workflow.Domain.Workflows;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// Administering a tenant's own workflow definitions: create, draft a version, publish it. No
/// definition ships pre-seeded in v1 - every tenant builds its own, same as
/// <c>NotificationTemplate</c>'s own note on platform defaults not existing yet.
/// </summary>
public sealed class WorkflowDefinitionService(IWorkflowDefinitionRepository definitions, IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions ValidationJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowDefinition> CreateAsync(
        string code, string name, string entityType, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition? existing = await definitions.GetByCodeAsync(code, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException("WORKFLOW_DEFINITION_CODE_IN_USE", $"A definition with code '{code}' already exists.");
        }

        WorkflowDefinition definition = WorkflowDefinition.Create(code, name, entityType);
        definitions.Add(definition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return definition;
    }

    public async Task<WorkflowDefinitionVersion> CreateDraftVersionAsync(
        Guid definitionId, string definitionJson, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await definitions.GetAsync(definitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinition), definitionId);

        ValidateShape(definitionJson);

        int nextVersion = await definitions.GetNextVersionNumberAsync(definition.Id, cancellationToken);
        WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(definition.Id, nextVersion, definitionJson);
        definitions.Add(version);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<WorkflowDefinitionVersion> UpdateDraftVersionAsync(
        Guid versionId, string definitionJson, CancellationToken cancellationToken = default)
    {
        WorkflowDefinitionVersion version = await definitions.GetVersionAsync(versionId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinitionVersion), versionId);

        ValidateShape(definitionJson);
        version.UpdateDraft(definitionJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return version;
    }

    /// <summary>
    /// Publishes a draft and retires whatever was published before it - only one published
    /// version per definition at a time, matching <c>WorkflowEngine.StartAsync</c>'s assumption
    /// that <c>GetPublishedVersionAsync</c> returns at most one row. Instances already running
    /// stay pinned to the version they started with regardless.
    /// </summary>
    public async Task<WorkflowDefinitionVersion> PublishAsync(Guid versionId, DateTime now, CancellationToken cancellationToken = default)
    {
        WorkflowDefinitionVersion version = await definitions.GetVersionAsync(versionId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinitionVersion), versionId);

        WorkflowDefinitionVersion? currentlyPublished = await definitions.GetPublishedVersionAsync(version.DefinitionId, cancellationToken);
        currentlyPublished?.Retire();

        version.Publish(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return version;
    }

    /// <summary>Structural validation only - real fields, no unknown top-level shape - not a full
    /// schema check. A step referencing a non-existent approver group or an invalid condition
    /// surfaces at resolution time (<see cref="ApproverResolver"/>,
    /// <see cref="WorkflowConditionEvaluator"/>) with a specific error, not here.</summary>
    private static void ValidateShape(string definitionJson)
    {
        WorkflowDefinitionModel? model;

        try
        {
            model = JsonSerializer.Deserialize<WorkflowDefinitionModel>(definitionJson, ValidationJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException("WORKFLOW_DEFINITION_INVALID_JSON", $"The definition is not valid JSON: {ex.Message}");
        }

        if (model is null || model.Steps.Count == 0)
        {
            throw new DomainRuleException("WORKFLOW_DEFINITION_NO_STEPS", "A definition needs at least one step.");
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (WorkflowStepModel step in model.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Key))
            {
                throw new DomainRuleException("WORKFLOW_DEFINITION_STEP_KEY_REQUIRED", "Every step needs a key.");
            }

            if (!seenKeys.Add(step.Key))
            {
                throw new DomainRuleException("WORKFLOW_DEFINITION_DUPLICATE_STEP_KEY", $"Step key '{step.Key}' is used more than once.");
            }
        }
    }
}
