using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

public enum CodeLinkKind
{
    Commit = 1,
    PullRequest = 2,
}

/// <summary>A commit or pull request that named a task by its code.</summary>
public sealed class TaskCodeLink : TenantAggregateRoot
{
    private TaskCodeLink()
    {
        Provider = null!;
        ExternalId = null!;
        Title = null!;
    }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public string Provider { get; private set; }

    public CodeLinkKind Kind { get; private set; }

    /// <summary>The commit's hash, or the pull request's repository and number.</summary>
    public string ExternalId { get; private set; }

    public string Title { get; private set; }

    public string? Url { get; private set; }

    public string? Author { get; private set; }

    public string? State { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static TaskCodeLink For(Guid projectId, Guid taskId, string provider, GitItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var link = new TaskCodeLink
        {
            ProjectId = Guard.NotEmpty(projectId),
            TaskId = Guard.NotEmpty(taskId),
            Provider = Guard.NotNullOrWhiteSpace(provider),
            Kind = item.Kind,
            ExternalId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(item.ExternalId), 200),
        };
        link.Refresh(item);
        return link;
    }

    /// <summary>A pull request changes: its title, its state, when it last moved.</summary>
    public void Refresh(GitItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Title = Short(item.Title, 300) ?? "(untitled)";
        Url = Short(item.Url, 1000);
        Author = Short(item.Author, 200);
        State = Short(item.State, 32);
        OccurredAt = item.OccurredAt;
    }

    private static string? Short(string? value, int most) => value is { Length: > 0 } ? (value.Length > most ? value[..most] : value) : null;
}

/// <summary>What a webhook said happened, and the tasks it named.</summary>
public sealed record GitItem(CodeLinkKind Kind, string ExternalId, string Title, string? Url, string? Author, string? State, DateTime OccurredAt, IReadOnlyList<string> TaskCodes);

/// <summary>
/// Reads what GitHub and Azure DevOps send, and checks it came from them. Pure, because a public endpoint's
/// rules deserve their own tests. Anything unrecognised yields nothing rather than an error, so a new event
/// type on their side cannot put a delivery into endless retries.
/// </summary>
public static partial class GitEvents
{
    public const string GitHub = "github";
    public const string AzureDevOps = "azure-devops";

    /// <summary>Whether a GitHub <c>X-Hub-Signature-256</c> header signs this body with this secret.</summary>
    public static bool IsSignedByGitHub(string secret, ReadOnlySpan<byte> body, string? header)
    {
        ArgumentNullException.ThrowIfNull(secret);
        const string Prefix = "sha256=";
        if (string.IsNullOrEmpty(secret) || header is null || !header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] expected = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(header[Prefix.Length..].ToLowerInvariant()));
    }

    /// <summary>Whether a Basic <c>Authorization</c> header carries this secret as its password, as an Azure DevOps service hook sends it.</summary>
    public static bool HasBasicPassword(string secret, string? header)
    {
        ArgumentNullException.ThrowIfNull(secret);
        const string Prefix = "Basic ";
        if (string.IsNullOrEmpty(secret) || header is null || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[Prefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        int colon = decoded.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
            SHA256.HashData(Encoding.UTF8.GetBytes(decoded[(colon + 1)..])));
    }

    /// <summary>Task codes in any of the text, in capitals: branches are often written in lower case.</summary>
    public static IReadOnlyList<string> CodesIn(params string?[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return [.. texts.OfType<string>().SelectMany(text => TaskCode().Matches(text)).Select(match => match.Value.ToUpperInvariant()).Distinct()];
    }

    /// <summary>A link kept only if it is plainly a web address, because it ends up behind an anchor in the app.</summary>
    public static string? SafeUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp) ? parsed.AbsoluteUri : null;

    public static IReadOnlyList<GitItem> FromGitHub(string? eventName, JsonElement root, DateTime now) => eventName switch
    {
        "push" => GitHubPush(root, now),
        "pull_request" => GitHubPullRequest(root, now),
        _ => [],
    };

    public static IReadOnlyList<GitItem> FromAzureDevOps(JsonElement root, DateTime now)
    {
        string? type = Text(root, "eventType");
        if (!Get(root, "resource", out JsonElement resource))
        {
            return [];
        }

        return type switch
        {
            "git.push" => AzurePush(resource, now),
            "git.pullrequest.created" or "git.pullrequest.updated" or "git.pullrequest.merged" => AzurePullRequest(resource, now),
            _ => [],
        };
    }

    private static List<GitItem> GitHubPush(JsonElement root, DateTime now)
    {
        string? branch = Text(root, "ref");
        List<GitItem> items = [];
        if (!Get(root, "commits", out JsonElement commits) || commits.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement commit in commits.EnumerateArray())
        {
            string? id = Text(commit, "id");
            string? message = Text(commit, "message");
            if (id is null)
            {
                continue;
            }

            IReadOnlyList<string> codes = CodesIn(message, branch);
            if (codes.Count > 0)
            {
                items.Add(new GitItem(
                    CodeLinkKind.Commit, id, FirstLine(message), SafeUrl(Text(commit, "url")), Get(commit, "author", out JsonElement author) ? Text(author, "name") : null, null, When(Text(commit, "timestamp"), now), codes));
            }
        }

        return items;
    }

    private static List<GitItem> GitHubPullRequest(JsonElement root, DateTime now)
    {
        if (!Get(root, "pull_request", out JsonElement pull) || !Get(pull, "number", out JsonElement number))
        {
            return [];
        }

        string? title = Text(pull, "title");
        string? branch = Get(pull, "head", out JsonElement head) ? Text(head, "ref") : null;
        IReadOnlyList<string> codes = CodesIn(title, branch, Text(pull, "body"));
        if (codes.Count == 0)
        {
            return [];
        }

        string repository = Get(root, "repository", out JsonElement repo) ? Text(repo, "full_name") ?? "repository" : "repository";
        bool merged = Get(pull, "merged", out JsonElement mergedFlag) && mergedFlag.ValueKind == JsonValueKind.True;
        return [new GitItem(
            CodeLinkKind.PullRequest, $"{repository}#{number}", FirstLine(title), SafeUrl(Text(pull, "html_url")), Get(pull, "user", out JsonElement user) ? Text(user, "login") : null,
            merged ? "merged" : Text(pull, "state"), When(Text(pull, "updated_at"), now), codes)];
    }

    private static List<GitItem> AzurePush(JsonElement resource, DateTime now)
    {
        List<GitItem> items = [];
        if (!Get(resource, "commits", out JsonElement commits) || commits.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        string? branch = Get(resource, "refUpdates", out JsonElement updates) && updates.ValueKind == JsonValueKind.Array && updates.GetArrayLength() > 0 ? Text(updates[0], "name") : null;
        string? remote = Get(resource, "repository", out JsonElement repo) ? Text(repo, "remoteUrl") : null;
        foreach (JsonElement commit in commits.EnumerateArray())
        {
            string? id = Text(commit, "commitId");
            string? message = Text(commit, "comment");
            IReadOnlyList<string> codes = CodesIn(message, branch);
            if (id is null || codes.Count == 0)
            {
                continue;
            }

            JsonElement author = default;
            bool hasAuthor = Get(commit, "author", out author);
            items.Add(new GitItem(
                CodeLinkKind.Commit, id, FirstLine(message), SafeUrl(remote is null ? Text(commit, "url") : $"{remote}/commit/{id}"), hasAuthor ? Text(author, "name") : null, null, When(hasAuthor ? Text(author, "date") : null, now), codes));
        }

        return items;
    }

    private static List<GitItem> AzurePullRequest(JsonElement resource, DateTime now)
    {
        if (!Get(resource, "pullRequestId", out JsonElement number))
        {
            return [];
        }

        string? title = Text(resource, "title");
        IReadOnlyList<string> codes = CodesIn(title, Text(resource, "sourceRefName"), Text(resource, "description"));
        if (codes.Count == 0)
        {
            return [];
        }

        string? web = Get(resource, "repository", out JsonElement repo) ? Text(repo, "webUrl") ?? Text(repo, "remoteUrl") : null;
        string repository = Get(resource, "repository", out JsonElement named) ? Text(named, "name") ?? "repository" : "repository";
        return [new GitItem(
            CodeLinkKind.PullRequest, $"{repository}#{number}", FirstLine(title), SafeUrl(web is null ? null : $"{web}/pullrequest/{number}"), Get(resource, "createdBy", out JsonElement by) ? Text(by, "displayName") : null,
            Text(resource, "status")?.ToLowerInvariant(), When(Text(resource, "closedDate") ?? Text(resource, "creationDate"), now), codes)];
    }

    private static bool Get(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    private static string? Text(JsonElement element, string name) => Get(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string FirstLine(string? message)
    {
        string text = message ?? string.Empty;
        int end = text.IndexOfAny(['\r', '\n']);
        return (end >= 0 ? text[..end] : text).Trim();
    }

    private static DateTime When(string? value, DateTime now)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed.UtcDateTime : now;

    // Letters and digits, a dash, a number; the word boundaries keep out the likes of utf-8 only by requiring two characters first.
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9_]{1,31}-\d{1,9}\b")]
    private static partial Regex TaskCode();
}

/// <summary>How concerning a signal is.</summary>
public enum RiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
}

public sealed record RiskSignal(RiskLevel Level, string Signal, string Detail);

public sealed record RiskTask(string Code, string Title, Guid? AssigneeEmployeeId, DateOnly? StartDate, DateOnly? DueDate, decimal? EstimatedHours, Guid? MilestoneId);

public sealed record RiskMilestone(Guid Id, string Name, DateOnly PlannedDate, bool IsAchieved);

public sealed record RiskPerson(string Name, decimal PlannedHours, decimal CapacityHours);

public sealed record RiskInput(
    DateOnly Today,
    IReadOnlyList<RiskTask> OpenTasks,
    IReadOnlyList<RiskMilestone> Milestones,
    IReadOnlyList<RiskPerson> PeopleNextWeeks,
    DateOnly EndDate,
    DateOnly? BaselineEndDate,
    int BrokenDependencies,
    int SlaBreachesThisMonth);

/// <summary>
/// What in a project's data is worth a manager's attention. These are facts counted by rules: the assistant
/// explains them and a person decides, which is why nothing here is called a prediction.
/// </summary>
public static class RiskSignals
{
    public static IReadOnlyList<RiskSignal> From(RiskInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<RiskSignal> signals = [];
        DateOnly today = input.Today;

        List<RiskTask> late = [.. input.OpenTasks.Where(task => task.DueDate < today)];
        if (late.Count > 0)
        {
            bool many = late.Count >= 5 || late.Count * 4 > input.OpenTasks.Count;
            signals.Add(new RiskSignal(many ? RiskLevel.High : RiskLevel.Medium, "Late tasks", $"{late.Count} of {input.OpenTasks.Count} open tasks are past due: {Sample(late)}."));
        }

        List<RiskMilestone> lateMilestones = [.. input.Milestones.Where(row => !row.IsAchieved && row.PlannedDate < today)];
        if (lateMilestones.Count > 0)
        {
            signals.Add(new RiskSignal(RiskLevel.High, "Late milestones", $"{string.Join(", ", lateMilestones.Select(row => row.Name))} should have been reached already."));
        }

        foreach (RiskMilestone soon in input.Milestones.Where(row => !row.IsAchieved && row.PlannedDate >= today && row.PlannedDate <= today.AddDays(14)))
        {
            int open = input.OpenTasks.Count(task => task.MilestoneId == soon.Id);
            if (open > 0)
            {
                signals.Add(new RiskSignal(RiskLevel.Medium, "Milestone close with work open", $"{soon.Name} is due {soon.PlannedDate:d MMM} with {open} open tasks against it."));
            }
        }

        if (input.BaselineEndDate is { } baseline && input.EndDate > baseline)
        {
            int days = input.EndDate.DayNumber - baseline.DayNumber;
            signals.Add(new RiskSignal(days > 14 ? RiskLevel.High : RiskLevel.Medium, "Schedule has slipped", $"The end date is {days} days past the baseline."));
        }

        if (input.SlaBreachesThisMonth > 0)
        {
            signals.Add(new RiskSignal(RiskLevel.High, "SLA breaches", $"{input.SlaBreachesThisMonth} tickets have missed a target this month."));
        }

        if (input.BrokenDependencies > 0)
        {
            signals.Add(new RiskSignal(RiskLevel.Medium, "Broken dependencies", $"{input.BrokenDependencies} tasks are scheduled before what they wait on finishes."));
        }

        List<RiskPerson> stretched = [.. input.PeopleNextWeeks.Where(person => person.PlannedHours > person.CapacityHours)];
        if (stretched.Count > 0)
        {
            signals.Add(new RiskSignal(RiskLevel.Medium, "People over capacity", string.Join(", ", stretched.Select(person => $"{person.Name} ({person.PlannedHours:0.#} h against {person.CapacityHours:0.#} h)")) + " over the next two weeks."));
        }

        List<RiskTask> nobody = [.. input.OpenTasks.Where(task => task.AssigneeEmployeeId is null && task.DueDate is { } due && due >= today && due <= today.AddDays(7))];
        if (nobody.Count > 0)
        {
            signals.Add(new RiskSignal(RiskLevel.Medium, "Due soon with nobody on it", $"{nobody.Count} tasks due within a week have no assignee: {Sample(nobody)}."));
        }

        int unestimated = input.OpenTasks.Count(task => task.EstimatedHours is null);
        if (input.OpenTasks.Count >= 5 && unestimated * 10 > input.OpenTasks.Count * 3)
        {
            signals.Add(new RiskSignal(RiskLevel.Low, "Work not estimated", $"{unestimated} of {input.OpenTasks.Count} open tasks have no estimate, so workload and forecast read low."));
        }

        return [.. signals.OrderByDescending(signal => signal.Level)];
    }

    private static string Sample(List<RiskTask> tasks)
    {
        string names = string.Join(", ", tasks.Take(5).Select(task => task.Code));
        return tasks.Count > 5 ? $"{names} and {tasks.Count - 5} more" : names;
    }
}
