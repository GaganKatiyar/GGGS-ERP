using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Studio.Domain;

/// <summary>Which of the tenant's connections sends its notifications on a channel: at most one per channel.</summary>
public sealed class ChannelRoute : TenantAggregateRoot
{
    private ChannelRoute()
    {
        Channel = null!;
    }

    public string Channel { get; private set; }

    public Guid ConnectionId { get; private set; }

    public static ChannelRoute Create(string channel, Guid connectionId) => new()
    {
        Channel = Guard.MaxLength(Guard.NotNullOrWhiteSpace(channel), 20),
        ConnectionId = Guard.NotEmpty(connectionId),
    };

    public void PointAt(Guid connectionId) => ConnectionId = Guard.NotEmpty(connectionId);
}
