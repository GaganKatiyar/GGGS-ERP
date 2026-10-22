using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Separation.Domain;

public enum ExitQuestionType
{
    /// <summary>1 (poor) to 5 (excellent).</summary>
    Rating = 1,

    YesNo = 2,

    /// <summary>One of the listed options.</summary>
    Choice = 3,

    Text = 4,
}

/// <summary>One question of the exit survey; <see cref="Key"/> ties answers to it across edits.</summary>
public sealed record ExitQuestion(string Key, string Text, ExitQuestionType Type, IReadOnlyList<string>? Options, bool Required);

/// <summary>
/// The organisation's exit survey: the questions a leaver answers before going. One per tenant,
/// created with sensible questions the first time it is read; HR edits it.
/// </summary>
public sealed class ExitSurvey : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ExitSurvey()
    {
        Title = null!;
        QuestionsJson = "[]";
    }

    public string Title { get; private set; }

    public string? Introduction { get; private set; }

    public string QuestionsJson { get; private set; }

    public IReadOnlyList<ExitQuestion> Questions => JsonSerializer.Deserialize<List<ExitQuestion>>(QuestionsJson, Json) ?? [];

    public static ExitSurvey CreateDefault()
    {
        var survey = new ExitSurvey();
        survey.Update(
            "Exit survey",
            "Thank you for your time with us. Your answers help us improve; you can hide them from your manager.",
            [
                new("overall", "How would you rate your time here overall?", ExitQuestionType.Rating, null, true),
                new("manager", "How well did your manager support you?", ExitQuestionType.Rating, null, true),
                new("growth", "How were your opportunities to learn and grow?", ExitQuestionType.Rating, null, true),
                new("pay", "How fair was your pay and benefits?", ExitQuestionType.Rating, null, false),
                new("main-reason", "What is the main reason you are leaving?", ExitQuestionType.Choice,
                    ["Career growth", "Compensation", "Manager", "Work-life balance", "Relocation", "Higher studies", "Health or family", "Culture", "Other"], true),
                new("recommend", "Would you recommend us as a place to work?", ExitQuestionType.YesNo, null, true),
                new("return", "Would you consider coming back?", ExitQuestionType.YesNo, null, false),
                new("improve", "What one thing should we change?", ExitQuestionType.Text, null, false),
            ]);
        return survey;
    }

    public void Update(string title, string? introduction, IReadOnlyList<ExitQuestion> questions)
    {
        List<ExitQuestion> cleaned = [.. (questions ?? []).Select(question => question with
        {
            Key = Guard.MaxLength(Guard.NotNullOrWhiteSpace(question.Key).Trim().ToLowerInvariant(), 40),
            Text = Guard.MaxLength(Guard.NotNullOrWhiteSpace(question.Text).Trim(), 300),
            Options = question.Type == ExitQuestionType.Choice ? [.. (question.Options ?? []).Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => Guard.MaxLength(option.Trim(), 100)).Distinct()] : null,
        })];
        if (cleaned.Count is 0 or > 30)
        {
            throw new DomainRuleException("EXIT_SURVEY_QUESTIONS", "An exit survey has between 1 and 30 questions.");
        }

        if (cleaned.Select(question => question.Key).Distinct().Count() != cleaned.Count)
        {
            throw new DomainRuleException("EXIT_SURVEY_QUESTIONS", "Each question needs its own key.");
        }

        if (cleaned.Any(question => !Enum.IsDefined(question.Type) || (question.Type == ExitQuestionType.Choice && question.Options!.Count < 2)))
        {
            throw new DomainRuleException("EXIT_SURVEY_QUESTIONS", "A choice question needs at least two options.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Introduction = string.IsNullOrWhiteSpace(introduction) ? null : Guard.MaxLength(introduction.Trim(), 1000);
        QuestionsJson = JsonSerializer.Serialize(cleaned, Json);
    }

    /// <summary>Checks answers against the questions and returns them cleaned (known keys only, required ones present).</summary>
    public IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string>? answers)
    {
        var given = new Dictionary<string, string>(answers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ExitQuestion question in Questions)
        {
            string? value = given.TryGetValue(question.Key, out string? raw) && !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : null;
            if (value is null)
            {
                if (question.Required)
                {
                    throw new DomainRuleException("EXIT_SURVEY_REQUIRED", $"Please answer: {question.Text}");
                }

                continue;
            }

            bool valid = question.Type switch
            {
                ExitQuestionType.Rating => int.TryParse(value, out int rating) && rating is >= 1 and <= 5,
                ExitQuestionType.YesNo => value is "yes" or "no",
                ExitQuestionType.Choice => question.Options!.Contains(value, StringComparer.Ordinal),
                _ => value.Length <= 2000,
            };
            if (!valid)
            {
                throw new DomainRuleException("EXIT_SURVEY_ANSWER", $"That is not an answer to: {question.Text}");
            }

            clean[question.Key] = value;
        }

        return clean;
    }
}

/// <summary>A leaver's answers to the exit survey, one per separation.</summary>
public sealed class ExitSurveyResponse : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ExitSurveyResponse()
    {
        AnswersJson = "{}";
    }

    public Guid CaseId { get; private set; }

    public string AnswersJson { get; private set; }

    /// <summary>The leaver asked that their manager not see these answers.</summary>
    public bool HiddenFromManager { get; private set; }

    public DateOnly SubmittedOn { get; private set; }

    public IReadOnlyDictionary<string, string> Answers => JsonSerializer.Deserialize<Dictionary<string, string>>(AnswersJson, Json) ?? [];

    public static ExitSurveyResponse Submit(Guid caseId, IReadOnlyDictionary<string, string> answers, bool hiddenFromManager, DateOnly today)
    {
        var response = new ExitSurveyResponse { CaseId = Guard.NotEmpty(caseId) };
        response.Revise(answers, hiddenFromManager, today);
        return response;
    }

    public void Revise(IReadOnlyDictionary<string, string> answers, bool hiddenFromManager, DateOnly today)
    {
        AnswersJson = JsonSerializer.Serialize(answers, Json);
        HiddenFromManager = hiddenFromManager;
        SubmittedOn = today;
    }
}
