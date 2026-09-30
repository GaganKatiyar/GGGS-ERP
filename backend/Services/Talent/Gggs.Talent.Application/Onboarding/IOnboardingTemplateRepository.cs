using Gggs.Talent.Domain.Onboarding;

namespace Gggs.Talent.Application.Onboarding;

public interface IOnboardingTemplateRepository
{
    Task<OnboardingTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(OnboardingTemplate onboardingTemplate);

    Task<IReadOnlyList<OnboardingTemplateTask>> GetTasksAsync(Guid templateId, CancellationToken cancellationToken = default);

    void AddTask(OnboardingTemplateTask task);
}

public interface IOnboardingCaseRepository
{
    Task<OnboardingCase?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(OnboardingCase onboardingCase);

    Task<IReadOnlyList<OnboardingTask>> GetTasksAsync(Guid caseId, CancellationToken cancellationToken = default);

    Task<OnboardingTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    void AddTask(OnboardingTask task);
}
