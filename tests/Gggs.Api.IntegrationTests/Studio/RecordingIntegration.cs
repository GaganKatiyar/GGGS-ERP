using System.Collections.Concurrent;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// What the test connector was asked to send, across the API and the worker, so automation tests
/// can see what would have gone to Slack or Teams without the network.
/// </summary>
public sealed class RecordingIntegration
{
    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Sent { get; } = new();

    public ConcurrentQueue<RecordedAccount> Accounts { get; } = new();

    public ConcurrentQueue<string> Disabled { get; } = new();
}

/// <summary>An account the test directory was asked to make, password included so tests can look for it where it must not be.</summary>
public sealed record RecordedAccount(string ExternalId, string Login, string Password);

/// <summary>A directory that records instead of creating anything; a connection saved with "fail" = "yes" refuses.</summary>
internal sealed class RecordingAccountAdapter(RecordingIntegration log) : IAccountAdapter
{
    public const string AdapterKey = "test-directory";

    public string Key => AdapterKey;

    public string Name => "Test directory";

    public string Category => "Identity";

    public string Description => "Records the accounts it is asked for. Tests only.";

    public IReadOnlyList<IntegrationField> Fields { get; } = [new("fail", "Fail every request", Required: false)];

    public IReadOnlyList<IntegrationOperation> Operations { get; } = [];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(true, "Ready."));

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(true, "Nothing to do."));

    public Task<AccountOutcome> CreateAccountAsync(IReadOnlyDictionary<string, string> settings, AccountRequest account, CancellationToken cancellationToken = default)
    {
        if (settings.GetValueOrDefault("fail") == "yes")
        {
            return Task.FromResult(new AccountOutcome(false, "The directory was told to fail."));
        }

        var made = new RecordedAccount(Guid.NewGuid().ToString("N"), $"{account.EmployeeCode.ToLowerInvariant()}@directory.example", account.TemporaryPassword);
        log.Accounts.Enqueue(made);
        return Task.FromResult(new AccountOutcome(true, "Created.", made.ExternalId, made.Login, Created: true));
    }

    public Task<IntegrationResult> DisableAccountAsync(IReadOnlyDictionary<string, string> settings, string externalId, CancellationToken cancellationToken = default)
    {
        if (settings.GetValueOrDefault("fail") == "yes")
        {
            return Task.FromResult(new IntegrationResult(false, "The directory was told to fail."));
        }

        log.Disabled.Enqueue(externalId);
        return Task.FromResult(new IntegrationResult(true, "Disabled."));
    }
}

/// <summary>Records instead of emailing: what the tests see of the organisation's own email.</summary>
internal sealed class RecordingEmailAdapter(RecordingIntegration log) : RecordingChannelAdapter(log, [new("to", "To"), new("subject", "Subject"), new("body", "Message")])
{
    public const string AdapterKey = "test-email";

    public override string Key => AdapterKey;

    public override string Channel => IntegrationChannels.Email;
}

/// <summary>Records instead of texting: what the tests see of the organisation's text messages.</summary>
internal sealed class RecordingSmsAdapter(RecordingIntegration log) : RecordingChannelAdapter(log, [new("to", "To"), new("text", "Message")])
{
    public const string AdapterKey = "test-sms";

    public override string Key => AdapterKey;

    public override string Channel => IntegrationChannels.Sms;
}

internal abstract class RecordingChannelAdapter(RecordingIntegration log, IReadOnlyList<IntegrationField> parameters) : IIntegrationAdapter
{
    public abstract string Key { get; }

    public abstract string Channel { get; }

    public string Name => Key;

    public string Category => "Messaging";

    public string Description => "Records what it is asked to send. Tests only.";

    public bool Automatable => true;

    public IReadOnlyList<IntegrationField> Fields { get; } = [new("fail", "Fail every send", Required: false)];

    public IReadOnlyList<IntegrationOperation> Operations { get; } = [new("send", "Send", "Records the message.", parameters)];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(true, "Ready."));

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (settings.GetValueOrDefault("fail") == "yes")
        {
            return Task.FromResult(new IntegrationResult(false, "The recorder was told to fail."));
        }

        log.Sent.Enqueue(new Dictionary<string, string>(parameters));
        return Task.FromResult(new IntegrationResult(true, "Recorded.", Records: 1));
    }
}

/// <summary>A connector that records its sends; a connection saved with "fail" = "yes" refuses every one.</summary>
internal sealed class RecordingAdapter(RecordingIntegration log) : IIntegrationAdapter
{
    public const string AdapterKey = "test-recorder";

    public string Key => AdapterKey;

    public string Name => "Test recorder";

    public string Category => "Messaging";

    public string Description => "Records what it is asked to send. Tests only.";

    public bool Automatable => true;

    public IReadOnlyList<IntegrationField> Fields { get; } = [new("fail", "Fail every send", Required: false)];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
        [new("post", "Post a message", "Records the message.", [new("text", "Message"), new("title", "Title", Required: false)])];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new IntegrationResult(true, "Ready."));

    public Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (settings.GetValueOrDefault("fail") == "yes")
        {
            return Task.FromResult(new IntegrationResult(false, "The recorder was told to fail."));
        }

        log.Sent.Enqueue(new Dictionary<string, string>(parameters));
        return Task.FromResult(new IntegrationResult(true, "Recorded.", Records: 1));
    }
}
