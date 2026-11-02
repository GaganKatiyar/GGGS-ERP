using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Studio.Domain;
using Gggs.Modules.Studio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Studio.Application;

/// <summary>One channel on the Sending page: the connection set for it, and those it could use.</summary>
public sealed record ChannelItem(string Channel, Guid? ConnectionId, IReadOnlyList<ConnectionOption> Connections);

public sealed record ChannelBody(Guid? ConnectionId);

public sealed record ChannelTestBody(string To);

/// <summary>Sets which connection sends the tenant's email and SMS, and sends through it for the Notification module.</summary>
internal sealed class ChannelService(AppDbContext dbContext, IIntegrationConnections connections) : ITenantMessageSender
{
    private static readonly string[] Channels = [IntegrationChannels.Email, IntegrationChannels.Sms];

    public async Task<IReadOnlyList<ChannelItem>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectionOption> options = await connections.ListAsync(cancellationToken);
        Dictionary<string, Guid> routes = await dbContext.ChannelRoutes.AsNoTracking().ToDictionaryAsync(route => route.Channel, route => route.ConnectionId, cancellationToken);
        return [.. Channels.Select(channel => new ChannelItem(channel, routes.TryGetValue(channel, out Guid id) ? id : null, [.. options.Where(option => option.Channel == channel)]))];
    }

    /// <summary>Points the channel at a connection, or back at the system default when none is given.</summary>
    public async Task<IReadOnlyList<ChannelItem>> SetAsync(string channel, ChannelBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Channels.Contains(channel))
        {
            throw new NotFoundException("Channel", channel);
        }

        ChannelRoute? route = await dbContext.ChannelRoutes.FirstOrDefaultAsync(row => row.Channel == channel, cancellationToken);
        if (body.ConnectionId is not { } connectionId)
        {
            if (route is not null)
            {
                dbContext.ChannelRoutes.Remove(route);
            }
        }
        else
        {
            if (!(await connections.ListAsync(cancellationToken)).Any(option => option.Id == connectionId && option.Channel == channel))
            {
                throw new DomainRuleException("CHANNEL_CONNECTION", channel == IntegrationChannels.Email ? "That connection cannot send email." : "That connection cannot send text messages.");
            }

            if (route is null)
            {
                dbContext.ChannelRoutes.Add(ChannelRoute.Create(channel, connectionId));
            }
            else
            {
                route.PointAt(connectionId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    /// <summary>Sends one real message through the channel's connection, to an address the person typed.</summary>
    public async Task<ChannelSendResult> TestAsync(string channel, ChannelTestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.To))
        {
            throw new DomainRuleException("CHANNEL_TEST", "Say where the test should go.");
        }

        ChannelSendResult? result = channel switch
        {
            IntegrationChannels.Email => await SendEmailAsync(body.To.Trim(), "Nuts and Bolts test email", "This test came from Nuts and Bolts: your organisation's email is set up.", cancellationToken),
            IntegrationChannels.Sms => await SendSmsAsync(body.To.Trim(), "This test came from Nuts and Bolts: your organisation's text messages are set up.", cancellationToken),
            _ => throw new NotFoundException("Channel", channel),
        };
        return result ?? throw new DomainRuleException("CHANNEL_NONE", "Choose a connection that is switched on first.");
    }

    public async Task<bool> SendsSmsAsync(CancellationToken cancellationToken = default)
        => await ActiveConnectionAsync(IntegrationChannels.Sms, cancellationToken) is not null;

    public async Task<ChannelSendResult?> SendEmailAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        => await SendAsync(IntegrationChannels.Email, new Dictionary<string, string> { ["to"] = recipient, ["subject"] = subject, ["body"] = body }, EmailOperation.Key, cancellationToken);

    public async Task<ChannelSendResult?> SendSmsAsync(string recipient, string text, CancellationToken cancellationToken = default)
        => await SendAsync(IntegrationChannels.Sms, new Dictionary<string, string> { ["to"] = recipient, ["text"] = text }, SmsOperation.Key, cancellationToken);

    private async Task<ChannelSendResult?> SendAsync(string channel, Dictionary<string, string> parameters, string operation, CancellationToken cancellationToken)
    {
        if (await ActiveConnectionAsync(channel, cancellationToken) is not { } connection)
        {
            return null;
        }

        ConnectionRunResult result = await connections.RunAsync(connection.Id, operation, parameters, cancellationToken);
        return new ChannelSendResult(result.Succeeded, result.Message);
    }

    /// <summary>A connection someone switched off counts as none set: the channel goes back to its default rather than failing every message.</summary>
    private async Task<ConnectionOption?> ActiveConnectionAsync(string channel, CancellationToken cancellationToken)
    {
        ChannelRoute? route = await dbContext.ChannelRoutes.AsNoTracking().FirstOrDefaultAsync(row => row.Channel == channel, cancellationToken);
        return route is null
            ? null
            : (await connections.ListAsync(cancellationToken)).FirstOrDefault(option => option.Id == route.ConnectionId && option.IsActive && option.Channel == channel);
    }
}
