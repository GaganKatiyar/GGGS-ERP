using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Onboarding;

namespace Gggs.Talent.Application.Onboarding;

/// <summary>
/// Starting a case copies every <see cref="OnboardingTemplateTask"/> on the template into a real
/// <see cref="OnboardingTask"/> - see that type's own remarks on why a copy, not a live reference.
/// Completing a task recomputes the case's <see cref="OnboardingCase.CompletionPercent"/> from the
/// task rows themselves rather than incrementing a counter, so the percentage can never drift from
/// what actually happened.
/// </summary>
public sealed class OnboardingCaseService(
    IOnboardingCaseRepository cases, IOnboardingTemplateRepository templates, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<OnboardingCase> StartAsync(
        Guid employeeId, Guid templateId, DateOnly startDate, CancellationToken cancellationToken = default)
    {
        _ = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingTemplate), templateId);

        IReadOnlyList<OnboardingTemplateTask> templateTasks = await templates.GetTasksAsync(templateId, cancellationToken);

        Guard(templateTasks.Count > 0, "ONBOARDING_TEMPLATE_HAS_NO_TASKS", "This template has no tasks to onboard against.");

        OnboardingCase onboardingCase = OnboardingCase.Create(employeeId, templateId, startDate);
        cases.Add(onboardingCase);

        foreach (OnboardingTemplateTask templateTask in templateTasks)
        {
            OnboardingTask task = OnboardingTask.Create(
                onboardingCase.Id, templateTask.Title, templateTask.AssigneeRole,
                startDate.AddDays(templateTask.DueOffsetDays), templateTask.IsMandatory);

            cases.AddTask(task);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return onboardingCase;
    }

    public async Task<OnboardingTask> CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        OnboardingTask task = await cases.GetTaskAsync(taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingTask), taskId);

        task.Complete(clock.GetUtcNow().UtcDateTime);

        OnboardingCase onboardingCase = await cases.GetAsync(task.CaseId, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingCase), task.CaseId);

        IReadOnlyList<OnboardingTask> allTasks = await cases.GetTasksAsync(task.CaseId, cancellationToken);
        int completedCount = allTasks.Count(x => x.Status == OnboardingTaskStatus.Completed);
        onboardingCase.UpdateCompletion(completedCount, allTasks.Count);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<OnboardingCase> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        OnboardingCase onboardingCase = await cases.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingCase), id);

        onboardingCase.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return onboardingCase;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
