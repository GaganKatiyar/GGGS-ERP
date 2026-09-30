using Gggs.Talent.Application.Onboarding;
using Gggs.Talent.Domain.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

public sealed class OnboardingTemplateRepository(TalentDbContext dbContext) : IOnboardingTemplateRepository
{
    public async Task<OnboardingTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.OnboardingTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(OnboardingTemplate onboardingTemplate) => dbContext.OnboardingTemplates.Add(onboardingTemplate);

    public async Task<IReadOnlyList<OnboardingTemplateTask>> GetTasksAsync(Guid templateId, CancellationToken cancellationToken = default)
        => await dbContext.OnboardingTemplateTasks.Where(x => x.TemplateId == templateId).ToListAsync(cancellationToken);

    public void AddTask(OnboardingTemplateTask task) => dbContext.OnboardingTemplateTasks.Add(task);
}

public sealed class OnboardingCaseRepository(TalentDbContext dbContext) : IOnboardingCaseRepository
{
    public async Task<OnboardingCase?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.OnboardingCases.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(OnboardingCase onboardingCase) => dbContext.OnboardingCases.Add(onboardingCase);

    public async Task<IReadOnlyList<OnboardingTask>> GetTasksAsync(Guid caseId, CancellationToken cancellationToken = default)
        => await dbContext.OnboardingTasks.Where(x => x.CaseId == caseId).ToListAsync(cancellationToken);

    public async Task<OnboardingTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        => await dbContext.OnboardingTasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);

    public void AddTask(OnboardingTask task) => dbContext.OnboardingTasks.Add(task);
}
