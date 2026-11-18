using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>A worker process Hangfire knows about, and when it last said it was alive.</summary>
public sealed record JobServerInfo(string Name, int Workers, DateTime? LastHeartbeat);

/// <summary>A recurring job as the schedule holds it.</summary>
public sealed record RecurringJobInfo(string Id, string Cron, DateTime? LastExecution, DateTime? NextExecution, string? LastState, string? Error);

/// <summary>A job that exhausted its retries, waiting for someone to look at it.</summary>
public sealed record FailedJobInfo(string Id, string Job, DateTime? FailedAt, string? Reason, string? TenantId);

/// <summary>The schedule's overall counts.</summary>
public sealed record JobCounts(long Enqueued, long Processing, long Scheduled, long Failed, long Succeeded);

/// <summary>
/// Read-only view of the background work, for the platform console's health screen, plus "try again"
/// for a failed job. Reads Hangfire's own storage, so it works from a host that runs no job server.
/// </summary>
public interface IJobMonitor
{
    IReadOnlyList<JobServerInfo> Servers();

    IReadOnlyList<RecurringJobInfo> RecurringJobs();

    IReadOnlyList<FailedJobInfo> FailedJobs(int count = 50);

    JobCounts Counts();

    /// <summary>Queues a failed job again; false when it is no longer failed.</summary>
    bool Retry(string jobId);
}

internal sealed class HangfireJobMonitor(JobStorage storage, IBackgroundJobClient jobs) : IJobMonitor
{
    public IReadOnlyList<JobServerInfo> Servers()
        => [.. storage.GetMonitoringApi().Servers().Select(server => new JobServerInfo(server.Name, server.WorkersCount, server.Heartbeat))];

    public IReadOnlyList<RecurringJobInfo> RecurringJobs()
    {
        using IStorageConnection connection = storage.GetConnection();
        return [.. connection.GetRecurringJobs()
            .OrderBy(job => job.Id, StringComparer.Ordinal)
            .Select(job => new RecurringJobInfo(job.Id, job.Cron, job.LastExecution, job.NextExecution, job.LastJobState, job.Error))];
    }

    public IReadOnlyList<FailedJobInfo> FailedJobs(int count = 50)
    {
        JobList<FailedJobDto> failed = storage.GetMonitoringApi().FailedJobs(0, Math.Clamp(count, 1, 200));
        using IStorageConnection connection = storage.GetConnection();
        return [.. failed.Select(pair => new FailedJobInfo(
            pair.Key,
            pair.Value.Job is { } job ? $"{job.Type.Name}.{job.Method.Name}" : "Unknown job",
            pair.Value.FailedAt,
            pair.Value.ExceptionMessage,
            connection.GetJobParameter(pair.Key, TenantJobFilter.TenantParameterName)?.Trim('"')))];
    }

    public JobCounts Counts()
    {
        StatisticsDto statistics = storage.GetMonitoringApi().GetStatistics();
        return new JobCounts(statistics.Enqueued, statistics.Processing, statistics.Scheduled, statistics.Failed, statistics.Succeeded);
    }

    public bool Retry(string jobId) => jobs.Requeue(jobId);
}
