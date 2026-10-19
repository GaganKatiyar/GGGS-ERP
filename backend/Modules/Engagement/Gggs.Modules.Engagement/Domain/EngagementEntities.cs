using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Engagement.Domain;

public enum QuestionType
{
    /// <summary>Agreement on a 1-5 scale.</summary>
    Rating = 1,

    /// <summary>"How likely are you to recommend us as a place to work?" on 0-10.</summary>
    Enps = 2,

    Choice = 3,

    Text = 4,
}

public sealed record SurveyQuestion(string Key, string Text, QuestionType Type, IReadOnlyList<string>? Options = null, bool Required = true);

public enum SurveyStatus
{
    Draft = 1,
    Scheduled = 2,
    Open = 3,
    Closed = 4,
}

/// <summary>
/// An engagement or pulse survey (spec: engagement): questions including eNPS, an audience of
/// departments (or everyone), an open window, and optional anonymity - in which case responses
/// carry no employee and results show only for groups of at least <see cref="MinimumGroupSize"/>.
/// </summary>
public sealed class Survey : TenantAggregateRoot
{
    /// <summary>The smallest group whose answers are shown, so no one can be singled out.</summary>
    public const int MinimumGroupSize = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Survey()
    {
        Title = null!;
        QuestionsJson = "[]";
        AudienceJson = "[]";
    }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public bool IsAnonymous { get; private set; }

    public string QuestionsJson { get; private set; }

    /// <summary>Department ids invited; empty means everyone.</summary>
    public string AudienceJson { get; private set; }

    public DateOnly OpensOn { get; private set; }

    public DateOnly ClosesOn { get; private set; }

    public SurveyStatus Status { get; private set; }

    public int InvitedCount { get; private set; }

    public int ResponseCount { get; private set; }

    public IReadOnlyList<SurveyQuestion> Questions => JsonSerializer.Deserialize<List<SurveyQuestion>>(QuestionsJson, Json) ?? [];

    public IReadOnlyList<Guid> AudienceDepartmentIds => JsonSerializer.Deserialize<List<Guid>>(AudienceJson, Json) ?? [];

    public static Survey Draft(string title, string? description, bool anonymous, IReadOnlyList<SurveyQuestion> questions, IReadOnlyList<Guid>? departments, DateOnly opensOn, DateOnly closesOn)
    {
        var survey = new Survey { Status = SurveyStatus.Draft };
        survey.Update(title, description, anonymous, questions, departments, opensOn, closesOn);
        return survey;
    }

    public void Update(string title, string? description, bool anonymous, IReadOnlyList<SurveyQuestion> questions, IReadOnlyList<Guid>? departments, DateOnly opensOn, DateOnly closesOn)
    {
        if (Status != SurveyStatus.Draft)
        {
            throw new DomainRuleException("SURVEY_NOT_DRAFT", "Only a draft survey can be changed.");
        }

        if (closesOn < opensOn)
        {
            throw new DomainRuleException("SURVEY_WINDOW", "The survey must close on or after it opens.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 2000);
        IsAnonymous = anonymous;
        QuestionsJson = JsonSerializer.Serialize(Validate(questions), Json);
        AudienceJson = JsonSerializer.Serialize((departments ?? []).Distinct().ToList(), Json);
        OpensOn = opensOn;
        ClosesOn = closesOn;
    }

    /// <summary>Publishes the survey: it opens now if its window has started, otherwise on its open date.</summary>
    public bool Publish(DateOnly today)
    {
        if (Status != SurveyStatus.Draft)
        {
            throw new DomainRuleException("SURVEY_NOT_DRAFT", "The survey is already published.");
        }

        if (ClosesOn < today)
        {
            throw new DomainRuleException("SURVEY_WINDOW", "The survey's window has already passed.");
        }

        Status = SurveyStatus.Scheduled;
        return OpensOn <= today;
    }

    public void Open(int invited)
    {
        if (Status != SurveyStatus.Scheduled)
        {
            throw new DomainRuleException("SURVEY_OPEN", "Only a scheduled survey opens.");
        }

        Status = SurveyStatus.Open;
        InvitedCount = invited;
    }

    public void Close(DateOnly today)
    {
        if (Status is not (SurveyStatus.Open or SurveyStatus.Scheduled))
        {
            throw new DomainRuleException("SURVEY_CLOSE", "Only a published survey can be closed.");
        }

        Status = SurveyStatus.Closed;
        if (ClosesOn > today)
        {
            ClosesOn = today;
        }
    }

    public void Responded() => ResponseCount++;

    /// <summary>Checks a set of answers against the questions; returns them normalised.</summary>
    public Dictionary<string, string> CheckAnswers(IReadOnlyDictionary<string, string>? answers)
    {
        if (Status != SurveyStatus.Open)
        {
            throw new DomainRuleException("SURVEY_NOT_OPEN", "The survey is not open.");
        }

        Dictionary<string, string> clean = [];
        foreach (SurveyQuestion question in Questions)
        {
            string? value = answers is not null && answers.TryGetValue(question.Key, out string? given) ? given?.Trim() : null;
            if (string.IsNullOrEmpty(value))
            {
                if (question.Required)
                {
                    throw new DomainRuleException("SURVEY_ANSWER_REQUIRED", $"Answer \"{question.Text}\".");
                }

                continue;
            }

            bool valid = question.Type switch
            {
                QuestionType.Rating => int.TryParse(value, out int rating) && rating is >= 1 and <= 5,
                QuestionType.Enps => int.TryParse(value, out int score) && score is >= 0 and <= 10,
                QuestionType.Choice => question.Options?.Contains(value) == true,
                _ => value.Length <= 2000,
            };
            if (!valid)
            {
                throw new DomainRuleException("SURVEY_ANSWER_INVALID", $"The answer to \"{question.Text}\" is not valid.");
            }

            clean[question.Key] = value;
        }

        return clean;
    }

    private static List<SurveyQuestion> Validate(IReadOnlyList<SurveyQuestion>? questions)
    {
        List<SurveyQuestion> list = [.. questions ?? []];
        if (list.Count is 0 or > 50)
        {
            throw new DomainRuleException("SURVEY_QUESTIONS", "A survey has 1 to 50 questions.");
        }

        List<SurveyQuestion> clean = [];
        foreach ((SurveyQuestion question, int index) in list.Select((question, index) => (question, index)))
        {
            if (!Enum.IsDefined(question.Type))
            {
                throw new DomainRuleException("SURVEY_QUESTION_TYPE", "Every question needs a type.");
            }

            List<string>? options = question.Type == QuestionType.Choice
                ? [.. (question.Options ?? []).Select(option => option.Trim()).Where(option => option.Length > 0).Distinct()]
                : null;
            if (options is { Count: < 2 })
            {
                throw new DomainRuleException("SURVEY_QUESTION_OPTIONS", "A choice question needs at least two options.");
            }

            string key = string.IsNullOrWhiteSpace(question.Key) ? $"q{index + 1}" : question.Key.Trim();
            clean.Add(new SurveyQuestion(Guard.MaxLength(key, 40), Guard.MaxLength(Guard.NotNullOrWhiteSpace(question.Text).Trim(), 500), question.Type, options, question.Required));
        }

        if (clean.Select(question => question.Key).Distinct().Count() != clean.Count)
        {
            throw new DomainRuleException("SURVEY_QUESTION_KEYS", "Question keys must be unique.");
        }

        return clean;
    }
}

/// <summary>Who was asked to take a survey and whether they have - never which response is theirs on an anonymous survey.</summary>
public sealed class SurveyInvitation : TenantAggregateRoot
{
    public Guid SurveyId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateTime? RespondedAt { get; private set; }

    public DateTime? RemindedAt { get; private set; }

    public static SurveyInvitation Invite(Guid surveyId, Guid employeeId) => new() { SurveyId = surveyId, EmployeeId = employeeId };

    public void Respond(DateTime now)
    {
        if (RespondedAt is not null)
        {
            throw new DomainRuleException("SURVEY_ALREADY_RESPONDED", "You have already taken this survey.");
        }

        RespondedAt = now;
    }

    public void Reminded(DateTime now) => RemindedAt = now;
}

/// <summary>One set of answers; the employee is recorded only on a named survey.</summary>
public sealed class SurveyResponse : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private SurveyResponse()
    {
        AnswersJson = "{}";
    }

    public Guid SurveyId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    /// <summary>The respondent's department, for breakdowns that respect the minimum group size.</summary>
    public Guid? DepartmentId { get; private set; }

    public string AnswersJson { get; private set; }

    public IReadOnlyDictionary<string, string> Answers => JsonSerializer.Deserialize<Dictionary<string, string>>(AnswersJson, Json) ?? [];

    public static SurveyResponse Record(Survey survey, Guid employeeId, Guid? departmentId, Dictionary<string, string> answers)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return new SurveyResponse
        {
            SurveyId = survey.Id,
            EmployeeId = survey.IsAnonymous ? null : employeeId,
            DepartmentId = departmentId,
            AnswersJson = JsonSerializer.Serialize(answers, Json),
        };
    }
}

/// <summary>eNPS: the share of promoters (9-10) less the share of detractors (0-6), from -100 to 100.</summary>
public static class Enps
{
    public static (int Promoters, int Passives, int Detractors, double? Score) Of(IReadOnlyCollection<int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        int promoters = scores.Count(score => score >= 9);
        int detractors = scores.Count(score => score <= 6);
        int passives = scores.Count - promoters - detractors;
        return (promoters, passives, detractors, scores.Count == 0 ? null : Math.Round(100.0 * (promoters - detractors) / scores.Count, 1));
    }
}

internal static class EngagementDefaults
{
    /// <summary>A ready-made pulse a survey can start from.</summary>
    public static readonly SurveyQuestion[] Pulse =
    [
        new("enps", "How likely are you to recommend this company as a place to work?", QuestionType.Enps),
        new("manager", "My manager supports my growth.", QuestionType.Rating),
        new("workload", "My workload is manageable.", QuestionType.Rating),
        new("recognition", "I receive recognition for good work.", QuestionType.Rating),
        new("tools", "I have the tools and information I need to do my job.", QuestionType.Rating),
        new("stay", "I see myself working here in two years.", QuestionType.Choice, ["Yes", "Not sure", "No"]),
        new("improve", "What one thing would make this a better place to work?", QuestionType.Text, Required: false),
    ];
}
