using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Separation.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record ExitSurveyView(string Title, string? Introduction, IReadOnlyList<ExitQuestion> Questions);

public sealed record ExitSurveyBody(string Title, string? Introduction, IReadOnlyList<ExitQuestion> Questions);

public sealed record SurveyAnswerView(string Key, string Question, ExitQuestionType Type, string? Answer);

/// <summary>A leaver's survey answers; withheld (no answers) when they hid them from the manager who is looking.</summary>
public sealed record SurveyResponseView(DateOnly SubmittedOn, bool HiddenFromManager, bool Withheld, IReadOnlyList<SurveyAnswerView> Answers);

public sealed record MySurveyView(ExitSurveyView Survey, SurveyResponseView? Response, bool CanAnswer);

public sealed record SurveyAnswersBody(IReadOnlyDictionary<string, string> Answers, bool HiddenFromManager);

/// <summary>
/// The exit survey (Separation v2 phase E): HR's question set, the leaver's answers from
/// self-service - which they can hide from their manager - and the answers on the case.
/// </summary>
internal sealed class ExitSurveyService(AppDbContext dbContext, IEmployeeDirectory employees, ICurrentUser currentUser, ITenantClock clock, TimeProvider timeProvider)
{
    public async Task<ExitSurveyView> GetAsync(CancellationToken cancellationToken) => View(await SurveyAsync(cancellationToken));

    public async Task<ExitSurveyView> UpdateAsync(ExitSurveyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExitSurvey survey = await SurveyAsync(cancellationToken);
        survey.Update(body.Title, body.Introduction, body.Questions);
        await dbContext.SaveChangesAsync(cancellationToken);
        return View(survey);
    }

    public async Task<MySurveyView> MineAsync(CancellationToken cancellationToken)
    {
        ExitSurvey survey = await SurveyAsync(cancellationToken);
        SeparationCase? separation = await OwnCaseAsync(cancellationToken);
        ExitSurveyResponse? response = separation is null ? null : await dbContext.ExitSurveyResponses.AsNoTracking().FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken);
        return new MySurveyView(View(survey), response is null ? null : Response(survey, response, withheld: false), separation?.Status == CaseStatus.Approved);
    }

    public async Task<MySurveyView> SubmitAsync(SurveyAnswersBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = await OwnCaseAsync(cancellationToken) is { Status: CaseStatus.Approved } open
            ? open
            : throw new DomainRuleException("SEPARATION_NONE", "You have no approved separation to answer the exit survey for.");
        ExitSurvey survey = await SurveyAsync(cancellationToken);
        IReadOnlyDictionary<string, string> answers = survey.Validate(body.Answers);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        ExitSurveyResponse? response = await dbContext.ExitSurveyResponses.FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken);
        if (response is null)
        {
            dbContext.ExitSurveyResponses.Add(ExitSurveyResponse.Submit(separation.Id, answers, body.HiddenFromManager, today));
            dbContext.CaseEvents.Add(CaseEvent.Record(separation.Id, "survey", "Exit survey answered", body.HiddenFromManager ? "Hidden from the manager." : null, currentUser.EmployeeId, timeProvider.GetUtcNow().UtcDateTime));
        }
        else
        {
            response.Revise(answers, body.HiddenFromManager, today);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await MineAsync(cancellationToken);
    }

    /// <summary>The case's answers for whoever is looking: withheld from the leaver's manager if they asked.</summary>
    public async Task<SurveyResponseView?> ForCaseAsync(SeparationCase separation, CancellationToken cancellationToken)
    {
        ExitSurveyResponse? response = await dbContext.ExitSurveyResponses.AsNoTracking().FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken);
        if (response is null)
        {
            return null;
        }

        Guid? me = currentUser.EmployeeId;
        bool withheld = response.HiddenFromManager && me is not null && me != separation.EmployeeId
            && (await employees.GetAsync(separation.EmployeeId, cancellationToken))?.ManagerEmployeeId == me;
        return Response(await SurveyAsync(cancellationToken), response, withheld);
    }

    /// <summary>Every response for the given cases, with the survey they answer.</summary>
    public async Task<(ExitSurvey Survey, IReadOnlyList<ExitSurveyResponse> Responses)> ResponsesAsync(IReadOnlyCollection<Guid> caseIds, CancellationToken cancellationToken)
        => (await SurveyAsync(cancellationToken), await dbContext.ExitSurveyResponses.AsNoTracking().Where(row => caseIds.Contains(row.CaseId)).ToListAsync(cancellationToken));

    private static ExitSurveyView View(ExitSurvey survey) => new(survey.Title, survey.Introduction, survey.Questions);

    private static SurveyResponseView Response(ExitSurvey survey, ExitSurveyResponse response, bool withheld)
    {
        IReadOnlyDictionary<string, string> answers = response.Answers;
        return new SurveyResponseView(response.SubmittedOn, response.HiddenFromManager, withheld,
            withheld ? [] : [.. survey.Questions.Select(question => new SurveyAnswerView(question.Key, question.Text, question.Type, answers.GetValueOrDefault(question.Key)))]);
    }

    private async Task<ExitSurvey> SurveyAsync(CancellationToken cancellationToken)
    {
        ExitSurvey? survey = await dbContext.ExitSurveys.FirstOrDefaultAsync(cancellationToken);
        if (survey is null)
        {
            survey = ExitSurvey.CreateDefault();
            dbContext.ExitSurveys.Add(survey);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return survey;
    }

    private async Task<SeparationCase?> OwnCaseAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("SEPARATION_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        return await dbContext.SeparationCases.AsNoTracking()
            .Where(row => row.EmployeeId == me && (row.Status == CaseStatus.Approved || row.Status == CaseStatus.Completed))
            .OrderByDescending(row => row.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>The alumni network as Recruitment reads it, to recognise former employees who apply again.</summary>
internal sealed class AlumniDirectory(AppDbContext dbContext) : IAlumniDirectory
{
    public async Task<IReadOnlyList<AlumniMatch>> MatchAsync(IReadOnlyCollection<string> emails, IReadOnlyCollection<string> mobiles, CancellationToken cancellationToken = default)
    {
        List<string> mails = [.. (emails ?? []).Where(email => !string.IsNullOrWhiteSpace(email)).Select(email => email.Trim().ToLowerInvariant()).Distinct()];
        List<string> phones = [.. (mobiles ?? []).Select(AlumniProfile.Digits).OfType<string>().Distinct()];
        if (mails.Count == 0 && phones.Count == 0)
        {
            return [];
        }

        return [.. (await dbContext.Alumni.AsNoTracking()
                .Where(row => (row.Email != null && mails.Contains(row.Email)) || (row.Mobile != null && phones.Contains(row.Mobile)))
                .ToListAsync(cancellationToken))
            .Select(row => new AlumniMatch(row.Id, row.EmployeeId, row.Name, row.Email, row.Mobile, row.LastDesignation, row.ExitDate, SeparationService.Label(row.SeparationType), row.EligibleForRehire))];
    }
}
