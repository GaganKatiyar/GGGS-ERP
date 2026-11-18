using Gggs.Modules.Recruitment.Application;
using Gggs.Modules.Recruitment.Domain;
using Shouldly;

namespace Gggs.Hiring.UnitTests;

/// <summary>The next step on an application follows from its stage, interviews and latest offer.</summary>
public sealed class NextStepTests
{
    private static readonly DateTime Applied = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Moved = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EarlyStages_GiveTheRecruiterAFewDays()
    {
        NextSteps.For(ApplicationStage.Applied, Applied, Applied, [], null).ShouldBe(new NextStep("Review the application", new DateOnly(2026, 10, 22)));
        NextSteps.For(ApplicationStage.Screening, Applied, Moved, [], null).ShouldBe(new NextStep("Finish screening", new DateOnly(2026, 10, 30)));
        NextSteps.For(ApplicationStage.Shortlisted, Applied, Moved, [], null).ShouldBe(new NextStep("Schedule an interview", new DateOnly(2026, 10, 28)));
    }

    [Fact]
    public void AnInterviewThatHasHappened_WaitsOnFeedback_AndOneAhead_IsTheStep()
    {
        var past = new InterviewFact("Technical 1", new DateTime(2026, 10, 21, 5, 0, 0, DateTimeKind.Utc), InterviewStatus.Scheduled);
        NextSteps.For(ApplicationStage.Interview, Applied, Moved, [past], null).ShouldBe(new NextStep("Feedback for Technical 1", new DateOnly(2026, 10, 22)));

        var ahead = new InterviewFact("Managerial", DateTime.UtcNow.AddDays(3), InterviewStatus.Scheduled);
        NextSteps.For(ApplicationStage.Interview, Applied, Moved, [ahead], null)!.Text.ShouldBe("Managerial interview");
    }

    [Fact]
    public void AfterTheLastCompletedRound_SomeoneDecides_AndWithNoRoundsOneIsScheduled()
    {
        var done = new InterviewFact("Technical 1", new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc), InterviewStatus.Completed);
        NextSteps.For(ApplicationStage.Interview, Applied, Moved, [done], null).ShouldBe(new NextStep("Decide after Technical 1", new DateOnly(2026, 10, 18)));
        NextSteps.For(ApplicationStage.Interview, Applied, Moved, [], null)!.Text.ShouldBe("Schedule the first round");
    }

    [Fact]
    public void AnOfferFollowsItsOwnStatus()
    {
        var created = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        OfferFact Offer(OfferStatus status, DateOnly? expires = null) => new(status, created, created.AddDays(1), expires, new DateOnly(2026, 11, 28));

        NextSteps.For(ApplicationStage.Offer, Applied, Moved, [], null)!.Text.ShouldBe("Draft the offer");
        NextSteps.For(ApplicationStage.Offer, Applied, Moved, [], Offer(OfferStatus.Sent, new DateOnly(2026, 11, 21))).ShouldBe(new NextStep("Offer sent, awaiting reply", new DateOnly(2026, 11, 21)));
        NextSteps.For(ApplicationStage.Offer, Applied, Moved, [], Offer(OfferStatus.Sent)).ShouldBe(new NextStep("Offer sent, awaiting reply", new DateOnly(2026, 11, 9)));
        NextSteps.For(ApplicationStage.Offer, Applied, Moved, [], Offer(OfferStatus.Accepted)).ShouldBe(new NextStep("Hire and start onboarding", new DateOnly(2026, 11, 28)));
    }

    [Theory]
    [InlineData(ApplicationStage.Hired)]
    [InlineData(ApplicationStage.Rejected)]
    [InlineData(ApplicationStage.Withdrawn)]
    public void AClosedApplication_HasNoNextStep(ApplicationStage stage)
        => NextSteps.For(stage, Applied, Moved, [], null).ShouldBeNull();
}
