using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Timesheets.Application;
using Gggs.Modules.Timesheets.Contracts;
using Shouldly;

namespace Gggs.Delivery.UnitTests;

public sealed class CodeLinkAndRiskRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    // ----- Who sent it ----------------------------------------------------------------------------

    private const string Secret = "a-long-enough-shared-secret";

    private static string GitHubHeader(string secret, byte[] body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    [Fact]
    public void GitHub_ASignatureMatchesOnlyThisBodyAndThisSecret()
    {
        byte[] body = Encoding.UTF8.GetBytes("{\"a\":1}");
        string good = GitHubHeader(Secret, body);

        GitEvents.IsSignedByGitHub(Secret, body, good).ShouldBeTrue();
        GitEvents.IsSignedByGitHub(Secret, body, good.ToUpperInvariant().Replace("SHA256=", "sha256=", StringComparison.Ordinal)).ShouldBeTrue();
        GitEvents.IsSignedByGitHub(Secret, Encoding.UTF8.GetBytes("{\"a\":2}"), good).ShouldBeFalse("the body was changed");
        GitEvents.IsSignedByGitHub("another-secret-of-some-length", body, good).ShouldBeFalse();
        GitEvents.IsSignedByGitHub(Secret, body, null).ShouldBeFalse();
        GitEvents.IsSignedByGitHub(Secret, body, "sha1=abc").ShouldBeFalse();
        GitEvents.IsSignedByGitHub(Secret, body, "sha256=").ShouldBeFalse();
        GitEvents.IsSignedByGitHub(string.Empty, body, GitHubHeader(string.Empty, body)).ShouldBeFalse("no secret must never mean anyone can sign");
    }

    private static string Basic(string credentials) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    [Fact]
    public void AzureDevOps_ThePasswordMustMatch_AndTheUserNameDoesNotMatter()
    {
        GitEvents.HasBasicPassword(Secret, Basic("azure:" + Secret)).ShouldBeTrue();
        GitEvents.HasBasicPassword(Secret, Basic(":" + Secret)).ShouldBeTrue();
        GitEvents.HasBasicPassword(Secret, Basic("azure:wrong")).ShouldBeFalse();
        GitEvents.HasBasicPassword(Secret, Basic(Secret)).ShouldBeFalse("no colon, so no password");
        GitEvents.HasBasicPassword(Secret, "Basic not-base64!!").ShouldBeFalse();
        GitEvents.HasBasicPassword(Secret, "Bearer " + Secret).ShouldBeFalse();
        GitEvents.HasBasicPassword(Secret, null).ShouldBeFalse();
        GitEvents.HasBasicPassword(string.Empty, Basic("azure:")).ShouldBeFalse();
    }

    // ----- What it says ---------------------------------------------------------------------------

    [Fact]
    public void TaskCodes_AreFoundInAnyCase_Once_AndNotInsideOtherWords()
    {
        GitEvents.CodesIn("Fix APOLLO-42 and apollo-42", "feature/nw_com-7-checkout").ShouldBe(["APOLLO-42", "NW_COM-7"]);
        GitEvents.CodesIn("nothing here", null).ShouldBeEmpty();
        GitEvents.CodesIn("version 2-3").ShouldBeEmpty();
        GitEvents.CodesIn("x-1").ShouldBeEmpty("a code needs at least two characters first");
    }

    [Theory]
    [InlineData("https://github.com/acme/app/commit/abc", true)]
    [InlineData("http://git.internal/acme/commit/abc", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    public void OnlyWebAddresses_AreKeptAsLinks(string url, bool kept)
        => (GitEvents.SafeUrl(url) is not null).ShouldBe(kept);

    [Fact]
    public void AGitHubPush_LinksEachCommitThatNamesATask()
    {
        JsonElement push = Json("""
            {"ref":"refs/heads/main","commits":[
              {"id":"abc","message":"APOLLO-42 fix the total\n\nLong story.","url":"https://github.com/a/b/commit/abc","timestamp":"2026-11-02T09:00:00Z","author":{"name":"Asha"}},
              {"id":"def","message":"Tidy up","url":"https://github.com/a/b/commit/def"}
            ]}
            """);

        GitItem item = GitEvents.FromGitHub("push", push, Now).Single();

        item.Kind.ShouldBe(CodeLinkKind.Commit);
        item.ExternalId.ShouldBe("abc");
        item.Title.ShouldBe("APOLLO-42 fix the total");
        item.Author.ShouldBe("Asha");
        item.TaskCodes.ShouldBe(["APOLLO-42"]);
        item.OccurredAt.ShouldBe(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ABranchNamingATask_LinksItsCommits()
        => GitEvents.FromGitHub("push", Json("""{"ref":"refs/heads/apollo-42-fix","commits":[{"id":"abc","message":"Fix"}]}"""), Now).Single().TaskCodes.ShouldBe(["APOLLO-42"]);

    [Fact]
    public void AGitHubPullRequest_ReadsItsState_AndMergedWins()
    {
        JsonElement pull = Json("""
            {"repository":{"full_name":"acme/app"},"pull_request":{"number":7,"title":"Checkout","body":"Closes APOLLO-9","state":"closed","merged":true,
             "html_url":"https://github.com/acme/app/pull/7","user":{"login":"asha"},"head":{"ref":"apollo-42"},"updated_at":"2026-11-02T09:00:00Z"}}
            """);

        GitItem item = GitEvents.FromGitHub("pull_request", pull, Now).Single();

        item.Kind.ShouldBe(CodeLinkKind.PullRequest);
        item.ExternalId.ShouldBe("acme/app#7");
        item.State.ShouldBe("merged");
        item.TaskCodes.ShouldBe(["APOLLO-42", "APOLLO-9"]);
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("issues")]
    [InlineData(null)]
    public void OtherGitHubEvents_AreIgnoredQuietly(string? name)
        => GitEvents.FromGitHub(name, Json("""{"zen":"Keep it simple"}"""), Now).ShouldBeEmpty();

    [Fact]
    public void ABadlyShapedPayload_YieldsNothingRatherThanFailing()
    {
        GitEvents.FromGitHub("push", Json("""{"commits":"not a list"}"""), Now).ShouldBeEmpty();
        GitEvents.FromGitHub("push", Json("""{"commits":[{"message":"APOLLO-1 no id"}]}"""), Now).ShouldBeEmpty();
        GitEvents.FromGitHub("pull_request", Json("""{"pull_request":{"title":"APOLLO-1"}}"""), Now).ShouldBeEmpty();
        GitEvents.FromAzureDevOps(Json("""{"eventType":"git.push"}"""), Now).ShouldBeEmpty();
        GitEvents.FromAzureDevOps(Json("[]"), Now).ShouldBeEmpty();
    }

    [Fact]
    public void AnAzureDevOpsPush_BuildsItsLinkFromTheRepository()
    {
        JsonElement push = Json("""
            {"eventType":"git.push","resource":{"repository":{"remoteUrl":"https://dev.azure.com/acme/_git/app"},"refUpdates":[{"name":"refs/heads/main"}],
             "commits":[{"commitId":"abc","comment":"APOLLO-42 fix","author":{"name":"Asha","date":"2026-11-02T09:00:00Z"}}]}}
            """);

        GitItem item = GitEvents.FromAzureDevOps(push, Now).Single();

        item.Url.ShouldBe("https://dev.azure.com/acme/_git/app/commit/abc");
        item.Author.ShouldBe("Asha");
    }

    [Fact]
    public void AnAzureDevOpsPullRequest_IsRead()
    {
        JsonElement pull = Json("""
            {"eventType":"git.pullrequest.merged","resource":{"pullRequestId":7,"title":"APOLLO-42 checkout","status":"Completed","sourceRefName":"refs/heads/x",
             "createdBy":{"displayName":"Asha"},"repository":{"name":"app","webUrl":"https://dev.azure.com/acme/_git/app"}}}
            """);

        GitItem item = GitEvents.FromAzureDevOps(pull, Now).Single();

        item.ExternalId.ShouldBe("app#7");
        item.State.ShouldBe("completed");
        item.Url.ShouldBe("https://dev.azure.com/acme/_git/app/pullrequest/7");
    }

    [Fact]
    public void ALink_KeepsItsTitleToALength_AndTakesNewsOfAPullRequest()
    {
        var first = new GitItem(CodeLinkKind.PullRequest, "app#7", new string('t', 400), "https://x.example/7", "Asha", "open", Now, ["A-1"]);
        TaskCodeLink link = TaskCodeLink.For(Guid.NewGuid(), Guid.NewGuid(), GitEvents.GitHub, first);
        link.Title.Length.ShouldBe(300);

        link.Refresh(first with { Title = "Better", State = "merged" });

        link.Title.ShouldBe("Better");
        link.State.ShouldBe("merged");
    }

    // ----- Risk signals ---------------------------------------------------------------------------

    private static readonly DateOnly Today = new(2026, 11, 3);

    private static RiskTask Task(string code, int? dueIn = null, Guid? assignee = null, decimal? estimate = 8, Guid? milestone = null)
        => new(code, code, assignee ?? Guid.NewGuid(), null, dueIn is { } days ? Today.AddDays(days) : null, estimate, milestone);

    private static RiskInput Input(IReadOnlyList<RiskTask>? tasks = null, IReadOnlyList<RiskMilestone>? milestones = null, IReadOnlyList<RiskPerson>? people = null, DateOnly? baseline = null, int broken = 0, int breaches = 0)
        => new(Today, tasks ?? [], milestones ?? [], people ?? [], Today.AddDays(60), baseline, broken, breaches);

    [Fact]
    public void AHealthyProject_HasNothingToSay()
        => RiskSignals.From(Input([Task("A-1", 5)], [new RiskMilestone(Guid.NewGuid(), "Far off", Today.AddDays(60), false)])).ShouldBeEmpty();

    [Fact]
    public void LateTasks_AreMediumAtFirst_AndHighWhenThereAreMany()
    {
        List<RiskTask> onTime = [.. Enumerable.Range(0, 9).Select(index => Task($"A-{index}", 5))];
        RiskSignals.From(Input([.. onTime, Task("L-1", -1)])).Single().Level.ShouldBe(RiskLevel.Medium);

        RiskSignal many = RiskSignals.From(Input([.. onTime, .. Enumerable.Range(0, 6).Select(index => Task($"L-{index}", -1))])).Single();
        many.Level.ShouldBe(RiskLevel.High);
        many.Detail.ShouldContain("and 1 more");
    }

    [Fact]
    public void AMilestonePastItsDate_IsHigh_AndOneReachedIsNot()
    {
        RiskSignals.From(Input(milestones: [new RiskMilestone(Guid.NewGuid(), "Go live", Today.AddDays(-1), false)])).Single().Level.ShouldBe(RiskLevel.High);
        RiskSignals.From(Input(milestones: [new RiskMilestone(Guid.NewGuid(), "Go live", Today.AddDays(-1), true)])).ShouldBeEmpty();
    }

    [Fact]
    public void AMilestoneDueSoon_WithWorkOpenAgainstIt_IsFlagged()
    {
        Guid milestone = Guid.NewGuid();

        RiskSignal signal = RiskSignals.From(Input([Task("A-1", 5, milestone: milestone)], [new RiskMilestone(milestone, "Pilot", Today.AddDays(5), false)])).Single();

        signal.Signal.ShouldBe("Milestone close with work open");
        signal.Detail.ShouldContain("Pilot");
    }

    [Theory]
    [InlineData(10, RiskLevel.Medium)]
    [InlineData(20, RiskLevel.High)]
    public void SlippingPastTheBaseline_GrowsWithTheSlip(int days, RiskLevel level)
        => RiskSignals.From(Input(baseline: Today.AddDays(60 - days))).Single().Level.ShouldBe(level);

    [Fact]
    public void TheWorstComesFirst_AndEachKindIsCounted()
    {
        IReadOnlyList<RiskSignal> signals = RiskSignals.From(Input(
            [Task("N-1", 3, assignee: null) with { AssigneeEmployeeId = null }],
            people: [new RiskPerson("Asha", 90, 80), new RiskPerson("Ravi", 10, 80)],
            broken: 2,
            breaches: 1));

        signals[0].Signal.ShouldBe("SLA breaches");
        signals.Select(signal => signal.Signal).ShouldContain("Broken dependencies");
        signals.Single(signal => signal.Signal == "People over capacity").Detail.ShouldContain("Asha");
        signals.Single(signal => signal.Signal == "People over capacity").Detail.ShouldNotContain("Ravi");
        signals.Select(signal => signal.Signal).ShouldContain("Due soon with nobody on it");
    }

    [Fact]
    public void WorkMostlyUnestimated_IsALowSignal_OnlyOnceThereIsEnoughToJudge()
    {
        RiskSignals.From(Input([.. Enumerable.Range(0, 5).Select(index => Task($"A-{index}", 5, estimate: null))])).Single().Level.ShouldBe(RiskLevel.Low);
        RiskSignals.From(Input([Task("A-1", 5, estimate: null)])).ShouldBeEmpty();
    }

    // ----- A timesheet draft ----------------------------------------------------------------------

    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static DraftDay[] Week(params int[] closed) => [.. Enumerable.Range(0, 7).Select(offset => new DraftDay(Monday.AddDays(offset), offset < 5 && !closed.Contains(offset)))];

    private static DraftTarget Target() => new(Guid.NewGuid(), Guid.NewGuid(), HourCategory.Billable);

    [Fact]
    public void ADraft_FillsEachWorkingDay_SharedAcrossTheTasks()
    {
        IReadOnlyList<DraftLine> lines = TimesheetDraft.Build(Week(), [Target(), Target()], 8);

        lines.Count.ShouldBe(10);
        lines.Sum(line => line.Hours).ShouldBe(40);
        lines.ShouldAllBe(line => line.Hours == 4);
    }

    [Fact]
    public void LeaveAndHolidays_AreLeftOut()
    {
        IReadOnlyList<DraftLine> lines = TimesheetDraft.Build(Week(closed: [0, 2]), [Target()], 8);

        lines.Select(line => line.Date).ShouldBe([Monday.AddDays(1), Monday.AddDays(3), Monday.AddDays(4)]);
    }

    [Fact]
    public void ADayThatWillNotDivide_StillAddsUpExactly_InQuarterHours()
    {
        IReadOnlyList<DraftLine> lines = TimesheetDraft.Build([new DraftDay(Monday, true)], [Target(), Target(), Target()], 8);

        lines.Sum(line => line.Hours).ShouldBe(8);
        lines.Select(line => line.Hours).ShouldBe([2.75m, 2.75m, 2.5m]);
    }

    [Fact]
    public void WithNoTasks_ThereIsNoDraft()
        => TimesheetDraft.Build(Week(), [], 8).ShouldBeEmpty();
}
