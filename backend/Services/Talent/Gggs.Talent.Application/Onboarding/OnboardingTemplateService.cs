using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Onboarding;

namespace Gggs.Talent.Application.Onboarding;

public sealed class OnboardingTemplateService(IOnboardingTemplateRepository templates, IUnitOfWork unitOfWork)
{
    public async Task<OnboardingTemplate> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        OnboardingTemplate template = OnboardingTemplate.Create(name);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task<OnboardingTemplateTask> AddTaskAsync(
        Guid templateId, string title, OnboardingAssigneeRole assigneeRole, int dueOffsetDays, bool isMandatory,
        CancellationToken cancellationToken = default)
    {
        _ = await templates.GetAsync(templateId, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingTemplate), templateId);

        OnboardingTemplateTask task = OnboardingTemplateTask.Create(templateId, title, assigneeRole, dueOffsetDays, isMandatory);
        templates.AddTask(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return task;
    }
}
