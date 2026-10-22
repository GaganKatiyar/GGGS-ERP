using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Separation v2 phase E: the exit survey and alumni matching.</summary>
public sealed class ExitSurveyTests
{
    private static Dictionary<string, string> Complete() => new()
    {
        ["overall"] = "4",
        ["manager"] = "3",
        ["growth"] = "2",
        ["main-reason"] = "Career growth",
        ["recommend"] = "yes",
    };

    [Fact]
    public void The_default_survey_asks_ratings_a_reason_and_whether_they_would_recommend_us()
    {
        ExitSurvey survey = ExitSurvey.CreateDefault();
        survey.Questions.Count.ShouldBeGreaterThanOrEqualTo(6);
        survey.Questions.Single(question => question.Key == "main-reason").Options!.ShouldContain("Compensation");
    }

    [Fact]
    public void Answers_must_cover_required_questions_and_fit_their_type()
    {
        ExitSurvey survey = ExitSurvey.CreateDefault();
        Dictionary<string, string> extra = Complete();
        extra["unknown"] = "x";
        IReadOnlyDictionary<string, string> clean = survey.Validate(extra);
        clean.ShouldNotContainKey("unknown");
        clean["overall"].ShouldBe("4");

        Dictionary<string, string> missing = Complete();
        missing.Remove("manager");
        Should.Throw<DomainRuleException>(() => survey.Validate(missing)).ErrorCode.ShouldBe("EXIT_SURVEY_REQUIRED");

        Dictionary<string, string> outOfRange = Complete();
        outOfRange["overall"] = "7";
        Should.Throw<DomainRuleException>(() => survey.Validate(outOfRange)).ErrorCode.ShouldBe("EXIT_SURVEY_ANSWER");

        Dictionary<string, string> notAnOption = Complete();
        notAnOption["main-reason"] = "Bored";
        Should.Throw<DomainRuleException>(() => survey.Validate(notAnOption)).ErrorCode.ShouldBe("EXIT_SURVEY_ANSWER");
    }

    [Fact]
    public void Hr_edits_the_questions_within_limits()
    {
        ExitSurvey survey = ExitSurvey.CreateDefault();
        Should.Throw<DomainRuleException>(() => survey.Update("Exit", null, [new ExitQuestion("a", "One", ExitQuestionType.Rating, null, true), new ExitQuestion("A", "Two", ExitQuestionType.Text, null, false)]))
            .ErrorCode.ShouldBe("EXIT_SURVEY_QUESTIONS");
        Should.Throw<DomainRuleException>(() => survey.Update("Exit", null, [new ExitQuestion("pick", "Pick one", ExitQuestionType.Choice, ["Only"], true)]))
            .ErrorCode.ShouldBe("EXIT_SURVEY_QUESTIONS");

        survey.Update("Leaving survey", "  Thanks  ", [new ExitQuestion("Overall", "Overall?", ExitQuestionType.Rating, ["ignored"], true)]);
        survey.Title.ShouldBe("Leaving survey");
        survey.Questions.Single().Key.ShouldBe("overall");
        survey.Questions.Single().Options.ShouldBeNull("only choice questions keep options");
    }

    [Fact]
    public void Alumni_mobiles_are_compared_on_their_digits()
    {
        AlumniProfile.Digits("+91 98450-12345").ShouldBe("919845012345");
        AlumniProfile.Digits("123").ShouldBeNull();

        AlumniProfile alumnus = AlumniProfile.Create(Guid.NewGuid(), "Asha", "asha@corp.example", "Engineer", null, new DateOnly(2026, 1, 31), SeparationType.Resignation, true);
        alumnus.Contact(" Asha.Rao@Example.com ", "98450 12345");
        alumnus.Email.ShouldBe("asha.rao@example.com", "the personal email replaces the work one");
        alumnus.Mobile.ShouldBe("9845012345");
    }
}
