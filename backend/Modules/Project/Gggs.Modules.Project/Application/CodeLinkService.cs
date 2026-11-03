using System.Text.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record CodeLinkItem(string Provider, CodeLinkKind Kind, string ExternalId, string Title, string? Url, string? Author, string? State, DateTime OccurredAt);

public sealed record CodeLinkSetup(Guid ConnectionId, string Name, string AdapterKey, bool IsActive, string WebhookPath);

/// <summary>
/// Commits and pull requests that name a task by its code. The connection holds the secret; the webhook is
/// public, so it answers every failure the same way and tells a caller nothing about what exists.
/// </summary>
internal sealed class CodeLinkService(
    AppDbContext dbContext,
    ITenantRegistry tenants,
    ITenantContextSetter tenantSetter,
    ITenantContext tenantContext,
    IIntegrationConnections connections,
    TimeProvider timeProvider)
{
    /// <summary>GitHub's own limit on a webhook payload.</summary>
    public const int MostBytes = 25 * 1024 * 1024;

    public async Task<IResult> ReceiveAsync(string tenantCode, Guid connectionId, HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength > MostBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        byte[]? body = await ReadAsync(request, cancellationToken);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        TenantInfo? tenant = await tenants.GetByCodeAsync(tenantCode, cancellationToken);
        if (tenant is null || !tenant.IsOperational)
        {
            return Results.Unauthorized();
        }

        tenantSetter.SetTenant(tenant);
        InboundConnection? connection = await connections.InboundAsync(connectionId, cancellationToken);
        bool verified = connection?.AdapterKey switch
        {
            GitEvents.GitHub => GitEvents.IsSignedByGitHub(connection.Secret, body, request.Headers["X-Hub-Signature-256"]),
            GitEvents.AzureDevOps => GitEvents.HasBasicPassword(connection.Secret, request.Headers.Authorization),
            _ => false,
        };
        if (!verified)
        {
            return Results.Unauthorized();
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<GitItem> items;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            items = connection!.AdapterKey == GitEvents.GitHub
                ? GitEvents.FromGitHub(request.Headers["X-GitHub-Event"], document.RootElement, now)
                : GitEvents.FromAzureDevOps(document.RootElement, now);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        return Results.Ok(new { linked = await LinkAsync(connection!.AdapterKey, items, cancellationToken) });
    }

    /// <summary>The connections that can send links, and the address to give each.</summary>
    public async Task<IReadOnlyList<CodeLinkSetup>> SetupAsync(CancellationToken cancellationToken)
    {
        string code = tenantContext.Tenant?.Code ?? string.Empty;
        IReadOnlyList<ConnectionOption> options = await connections.ConnectionsToAsync([GitEvents.GitHub, GitEvents.AzureDevOps], cancellationToken);
        return [.. options
            .Select(option => new CodeLinkSetup(option.Id, option.Name, option.AdapterKey, option.IsActive, $"/api/v1/webhooks/git/{code}/{option.Id}"))];
    }

    public async Task<IReadOnlyList<CodeLinkItem>> ForTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        List<TaskCodeLink> links = await dbContext.TaskCodeLinks.AsNoTracking().Where(row => row.TaskId == taskId).OrderByDescending(row => row.OccurredAt).Take(100).ToListAsync(cancellationToken);
        return [.. links.Select(row => new CodeLinkItem(row.Provider, row.Kind, row.ExternalId, row.Title, row.Url, row.Author, row.State, row.OccurredAt))];
    }

    private static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MostBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private async Task<int> LinkAsync(string provider, IReadOnlyList<GitItem> items, CancellationToken cancellationToken)
    {
        List<string> codes = [.. items.SelectMany(item => item.TaskCodes).Distinct()];
        if (codes.Count == 0)
        {
            return 0;
        }

        var tasks = await dbContext.ProjectTasks.AsNoTracking().Where(row => codes.Contains(row.Code)).Select(row => new { row.Id, row.Code, row.ProjectId }).ToListAsync(cancellationToken);
        List<Guid> taskIds = [.. tasks.Select(row => row.Id)];
        List<TaskCodeLink> existing = await dbContext.TaskCodeLinks.Where(row => taskIds.Contains(row.TaskId) && row.Provider == provider).ToListAsync(cancellationToken);

        int linked = 0;
        foreach (GitItem item in items)
        {
            foreach (var task in tasks.Where(row => item.TaskCodes.Contains(row.Code, StringComparer.OrdinalIgnoreCase)))
            {
                TaskCodeLink? known = existing.Find(row => row.TaskId == task.Id && row.Kind == item.Kind && row.ExternalId == item.ExternalId);
                if (known is not null)
                {
                    known.Refresh(item);
                    continue;
                }

                TaskCodeLink link = TaskCodeLink.For(task.ProjectId, task.Id, provider, item);
                dbContext.TaskCodeLinks.Add(link);
                existing.Add(link);
                linked++;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same delivery arriving twice at once: the other one has recorded it.
            return 0;
        }

        return linked;
    }
}

/// <summary>A code host that reports commits and pull requests. It sends to us, so its only setting is the secret both ends share.</summary>
internal abstract class CodeHostAdapter : IIntegrationAdapter
{
    public abstract string Key { get; }

    public abstract string Name { get; }

    public string Category => "Engineering";

    public abstract string Description { get; }

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("secret", "Webhook secret", Secret: true, Hint: "At least 16 characters. The same value goes in the webhook on the other side."),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } = [];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool good = settings.GetValueOrDefault("secret") is { Length: >= 16 };
        return Task.FromResult(new IntegrationResult(
            good,
            good ? "The secret is long enough. Its webhook address is under Project settings. Nothing has been sent: the other side sends to us." : "The webhook secret must be at least 16 characters."));
    }

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(false, "This connection has nothing to run: it receives."));
}

internal sealed class GitHubAdapter : CodeHostAdapter
{
    public override string Key => GitEvents.GitHub;

    public override string Name => "GitHub";

    public override string Description => "Links commits and pull requests to tasks when they name a task code, such as APOLLO-42. Receives push and pull request webhooks, signed with a shared secret.";
}

internal sealed class AzureDevOpsAdapter : CodeHostAdapter
{
    public override string Key => GitEvents.AzureDevOps;

    public override string Name => "Azure DevOps";

    public override string Description => "Links commits and pull requests to tasks when they name a task code, such as APOLLO-42. Receives service hooks with basic authentication, using the secret as the password.";
}
