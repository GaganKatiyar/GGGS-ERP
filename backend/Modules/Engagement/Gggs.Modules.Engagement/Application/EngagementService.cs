using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Engagement.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Engagement.Application;

public sealed record SurveyItem(
    Guid Id,
    string Title,
    string? Description,
    bool IsAnonymous,
    DateOnly OpensOn,
    DateOnly ClosesOn,
    SurveyStatus Status,
    int Questions,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    int InvitedCount,
    int ResponseCount,
    double ResponseRate);

public sealed record SurveyView(SurveyItem Survey, IReadOnlyList<SurveyQuestion> Questions);

public sealed record SurveyBody(string Title, string? Description, bool IsAnonymous, IReadOnlyList<SurveyQuestion> Questions, IReadOnlyList<Guid>? AudienceDepartmentIds, DateOnly OpensOn, DateOnly ClosesOn);

public sealed record MySurveyItem(Guid Id, string Title, string? Description, bool IsAnonymous, DateOnly ClosesOn, bool Responded, IReadOnlyList<SurveyQuestion> Questions);

public sealed record RespondBody(IReadOnlyDictionary<string, string> Answers);

public sealed record QuestionResult(
    string Key,
    string Text,
    QuestionType Type,
    int Answers,
    double? Average,
    IReadOnlyDictionary<string, int> Distribution,
    double? EnpsScore,
    IReadOnlyList<string> Comments);

public sealed record GroupResult(Guid? DepartmentId, string Department, int Responses, double? Enps, double? AverageRating);

public sealed record SurveyResults(
    SurveyItem Survey,
    bool Suppressed,
    int MinimumGroupSize,
    double? Enps,
    int Promoters,
    int Passives,
    int Detractors,
    IReadOnlyList<QuestionResult> Questions,
    IReadOnlyList<GroupResult> ByDepartment,
    int SuppressedGroups);

public sealed record EnpsPoint(Guid SurveyId, string Title, DateOnly ClosesOn, int Responses, double? Enps);

public sealed record EngagementDashboard(int OpenSurveys, double? LatestEnps, double AverageResponseRate, IReadOnlyList<EnpsPoint> EnpsTrend);

/// <summary>
/// Engagement surveys and eNPS (spec: engagement): surveys built from rating, eNPS, choice and text
/// questions, sent to departments or everyone, answered in a window - anonymously if chosen - and
/// reported only for groups large enough that no one can be identified.
/// </summary>
internal sealed class EngagementService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public static IReadOnlyList<SurveyQuestion> PulseTemplate => EngagementDefaults.Pulse;

    public async Task<IReadOnlyList<SurveyItem>> SurveysAsync(SurveyStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<Survey> rows = dbContext.Surveys.AsNoTracking();
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return [.. (await rows.OrderByDescending(row => row.OpensOn).ToListAsync(cancellationToken)).Select(Item)];
    }

    public async Task<SurveyView> SurveyAsync(Guid id, CancellationToken cancellationToken)
    {
        Survey survey = await dbContext.Surveys.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Survey), id);
        return new SurveyView(Item(survey), survey.Questions);
    }

    public async Task<SurveyView> SaveAsync(Guid? id, SurveyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Survey survey;
        if (id is { } existing)
        {
            survey = await dbContext.Surveys.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(Survey), existing);
            survey.Update(body.Title, body.Description, body.IsAnonymous, body.Questions, body.AudienceDepartmentIds, body.OpensOn, body.ClosesOn);
        }
        else
        {
            survey = Survey.Draft(body.Title, body.Description, body.IsAnonymous, body.Questions, body.AudienceDepartmentIds, body.OpensOn, body.ClosesOn);
            dbContext.Surveys.Add(survey);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SurveyView(Item(survey), survey.Questions);
    }

    /// <summary>Publishes a draft; it opens at once (inviting its audience) if its window has started.</summary>
    public async Task<SurveyView> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        Survey survey = await dbContext.Surveys.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Survey), id);
        if (survey.Publish(await clock.TodayAsync(cancellationToken)))
        {
            await OpenAsync(survey, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SurveyView(Item(survey), survey.Questions);
    }

    public async Task<SurveyView> CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        Survey survey = await dbContext.Surveys.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Survey), id);
        survey.Close(await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SurveyView(Item(survey), survey.Questions);
    }

    public async Task<IReadOnlyList<MySurveyItem>> MySurveysAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        var rows = await (
                from invitation in dbContext.SurveyInvitations.AsNoTracking()
                join survey in dbContext.Surveys.AsNoTracking() on invitation.SurveyId equals survey.Id
                where invitation.EmployeeId == me && survey.Status == SurveyStatus.Open
                orderby survey.ClosesOn
                select new { Survey = survey, invitation.RespondedAt })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new MySurveyItem(row.Survey.Id, row.Survey.Title, row.Survey.Description, row.Survey.IsAnonymous, row.Survey.ClosesOn, row.RespondedAt != null, row.Survey.Questions))];
    }

    public async Task RespondAsync(Guid id, IReadOnlyDictionary<string, string>? answers, CancellationToken cancellationToken)
    {
        Guid me = Me();
        Survey survey = await dbContext.Surveys.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Survey), id);
        SurveyInvitation invitation = await dbContext.SurveyInvitations.FirstOrDefaultAsync(row => row.SurveyId == id && row.EmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(Survey), id);
        Dictionary<string, string> clean = survey.CheckAnswers(answers);
        invitation.Respond(Now);
        EmployeeSummary? person = await employees.GetAsync(me, cancellationToken);
        dbContext.SurveyResponses.Add(SurveyResponse.Record(survey, me, person?.DepartmentId, clean));
        survey.Responded();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Results for a survey. Nothing is shown until <see cref="Survey.MinimumGroupSize"/> people
    /// have answered; department breakdowns and comments follow the same rule.
    /// </summary>
    public async Task<SurveyResults> ResultsAsync(Guid id, CancellationToken cancellationToken)
    {
        Survey survey = await dbContext.Surveys.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Survey), id);
        List<SurveyResponse> responses = await dbContext.SurveyResponses.AsNoTracking().Where(row => row.SurveyId == id).ToListAsync(cancellationToken);
        IReadOnlyList<SurveyQuestion> questions = survey.Questions;
        if (responses.Count < Survey.MinimumGroupSize)
        {
            return new SurveyResults(Item(survey), true, Survey.MinimumGroupSize, null, 0, 0, 0, [], [], 0);
        }

        List<IReadOnlyDictionary<string, string>> answers = [.. responses.Select(row => row.Answers)];
        string? enpsKey = questions.FirstOrDefault(question => question.Type == QuestionType.Enps)?.Key;
        (int promoters, int passives, int detractors, double? enps) = Enps.Of(Scores(answers, enpsKey));

        List<QuestionResult> results = [];
        foreach (SurveyQuestion question in questions)
        {
            List<string> given = [.. answers.Where(set => set.ContainsKey(question.Key)).Select(set => set[question.Key])];
            List<int> numbers = [.. given.Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? (int?)number : null).Where(number => number != null).Select(number => number!.Value)];
            Dictionary<string, int> distribution = question.Type == QuestionType.Text
                ? []
                : given.GroupBy(value => value).OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count());
            results.Add(new QuestionResult(
                question.Key,
                question.Text,
                question.Type,
                given.Count,
                question.Type is QuestionType.Rating or QuestionType.Enps && numbers.Count > 0 ? Math.Round(numbers.Average(), 2) : null,
                distribution,
                question.Type == QuestionType.Enps ? Enps.Of(numbers).Score : null,
                question.Type == QuestionType.Text ? [.. given.OrderBy(_ => Guid.NewGuid())] : []));
        }

        List<string> ratingKeys = [.. questions.Where(question => question.Type == QuestionType.Rating).Select(question => question.Key)];
        IReadOnlyDictionary<Guid, string> departmentNames = await DepartmentNamesAsync(responses.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value), cancellationToken);
        List<GroupResult> groups = [];
        int suppressed = 0;
        foreach (IGrouping<Guid?, SurveyResponse> group in responses.GroupBy(row => row.DepartmentId))
        {
            if (group.Count() < Survey.MinimumGroupSize)
            {
                suppressed++;
                continue;
            }

            List<IReadOnlyDictionary<string, string>> set = [.. group.Select(row => row.Answers)];
            List<int> ratings = [.. set.SelectMany(answer => ratingKeys.Where(answer.ContainsKey).Select(key => int.Parse(answer[key], CultureInfo.InvariantCulture)))];
            groups.Add(new GroupResult(
                group.Key,
                group.Key is { } department ? departmentNames.GetValueOrDefault(department) ?? "Unknown" : "No department",
                group.Count(),
                Enps.Of(Scores(set, enpsKey)).Score,
                ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2)));
        }

        return new SurveyResults(Item(survey), false, Survey.MinimumGroupSize, enps, promoters, passives, detractors, results, [.. groups.OrderBy(group => group.Department, StringComparer.Ordinal)], suppressed);
    }

    public async Task<EngagementDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        List<Survey> surveys = await dbContext.Surveys.AsNoTracking().Where(row => row.Status == SurveyStatus.Open || row.Status == SurveyStatus.Closed).OrderBy(row => row.ClosesOn).ToListAsync(cancellationToken);
        List<EnpsPoint> trend = [];
        foreach (Survey survey in surveys.Where(row => row.Questions.Any(question => question.Type == QuestionType.Enps)).TakeLast(12))
        {
            string key = survey.Questions.First(question => question.Type == QuestionType.Enps).Key;
            List<string> json = await dbContext.SurveyResponses.AsNoTracking().Where(row => row.SurveyId == survey.Id).Select(row => row.AnswersJson).ToListAsync(cancellationToken);
            List<int> scores = Scores(json.Select(text => (IReadOnlyDictionary<string, string>)(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? [])), key);
            trend.Add(new EnpsPoint(survey.Id, survey.Title, survey.ClosesOn, json.Count, json.Count >= Survey.MinimumGroupSize ? Enps.Of(scores).Score : null));
        }

        List<Survey> withInvites = [.. surveys.Where(row => row.InvitedCount > 0)];
        return new EngagementDashboard(
            surveys.Count(row => row.Status == SurveyStatus.Open),
            trend.LastOrDefault(point => point.Enps != null)?.Enps,
            withInvites.Count == 0 ? 0 : Math.Round(withInvites.Average(row => 100.0 * row.ResponseCount / row.InvitedCount), 1),
            trend);
    }

    /// <summary>Opens scheduled surveys whose day has come, closes those past their window, and reminds people two days before close.</summary>
    public async Task<(int Opened, int Closed, int Reminded)> SweepAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Survey> toOpen = await dbContext.Surveys.Where(row => row.Status == SurveyStatus.Scheduled && row.OpensOn <= today).ToListAsync(cancellationToken);
        foreach (Survey survey in toOpen)
        {
            if (survey.ClosesOn < today)
            {
                survey.Close(today);
                continue;
            }

            await OpenAsync(survey, cancellationToken);
        }

        List<Survey> toClose = await dbContext.Surveys.Where(row => row.Status == SurveyStatus.Open && row.ClosesOn < today).ToListAsync(cancellationToken);
        foreach (Survey survey in toClose)
        {
            survey.Close(today);
        }

        DateOnly reminderDay = today.AddDays(2);
        List<Survey> closingSoon = await dbContext.Surveys.AsNoTracking().Where(row => row.Status == SurveyStatus.Open && row.ClosesOn == reminderDay).ToListAsync(cancellationToken);
        int reminded = 0;
        foreach (Survey survey in closingSoon)
        {
            List<SurveyInvitation> pending = await dbContext.SurveyInvitations.Where(row => row.SurveyId == survey.Id && row.RespondedAt == null && row.RemindedAt == null).ToListAsync(cancellationToken);
            foreach (SurveyInvitation invitation in pending)
            {
                invitation.Reminded(Now);
            }

            await NotifyOpenedAsync(survey, [.. pending.Select(row => row.EmployeeId)], cancellationToken);
            reminded += pending.Count;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (toOpen.Count, toClose.Count, reminded);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static List<int> Scores(IEnumerable<IReadOnlyDictionary<string, string>> answers, string? key)
        => key is null
            ? []
            : [.. answers.Where(set => set.ContainsKey(key)).Select(set => int.TryParse(set[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int score) ? (int?)score : null).Where(score => score != null).Select(score => score!.Value)];

    private static SurveyItem Item(Survey row)
        => new(row.Id, row.Title, row.Description, row.IsAnonymous, row.OpensOn, row.ClosesOn, row.Status, row.Questions.Count, row.AudienceDepartmentIds, row.InvitedCount, row.ResponseCount,
            row.InvitedCount == 0 ? 0 : Math.Round(100.0 * row.ResponseCount / row.InvitedCount, 1));

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("SURVEY_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task OpenAsync(Survey survey, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> departments = survey.AudienceDepartmentIds;
        IReadOnlyList<Guid> audience = await employees.ListActiveIdsAsync(departments.Count == 0 ? null : [.. departments], null, cancellationToken);
        HashSet<Guid> already = [.. await dbContext.SurveyInvitations.Where(row => row.SurveyId == survey.Id).Select(row => row.EmployeeId).ToListAsync(cancellationToken)];
        foreach (Guid employeeId in audience.Where(employeeId => !already.Contains(employeeId)))
        {
            dbContext.SurveyInvitations.Add(SurveyInvitation.Invite(survey.Id, employeeId));
        }

        survey.Open(audience.Count);
        await NotifyOpenedAsync(survey, audience, cancellationToken);
    }

    private Task NotifyOpenedAsync(Survey survey, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken)
        => recipients.Count == 0
            ? Task.CompletedTask
            : notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.SurveyOpened,
                    [.. recipients.Select(employeeId => new NotificationRecipient(employeeId))],
                    new Dictionary<string, string>
                    {
                        ["surveyTitle"] = survey.Title,
                        ["closesOn"] = survey.ClosesOn.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        ["anonymity"] = survey.IsAnonymous ? "Your answers are anonymous." : "Your answers are recorded with your name.",
                    },
                    "/me/surveys"),
                cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, string>> DepartmentNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        List<Guid> list = [.. ids.Distinct()];
        return list.Count == 0 ? new Dictionary<Guid, string>() : await organization.GetNamesAsync(OrgUnitKind.Department, list, cancellationToken);
    }
}
