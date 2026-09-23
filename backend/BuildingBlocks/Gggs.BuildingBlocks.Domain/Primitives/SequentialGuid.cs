namespace Gggs.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Generates GUIDs that SQL Server sorts in creation order (ADR-08).
/// <para>
/// SQL Server orders <c>uniqueidentifier</c> values by their last six bytes first, so the timestamp
/// is written there. That keeps the clustered index on (TenantId, Id) append-friendly instead of
/// fragmenting it the way a random GUID would, while the key stays globally unique - which is what
/// lets a tenant move between a pooled and a dedicated database without key collisions.
/// </para>
/// </summary>
public static class SequentialGuid
{
    /// <summary>Creates a new SQL-Server-ordered sequential GUID.</summary>
    public static Guid Create() => Create(DateTimeOffset.UtcNow);

    /// <summary>Creates a sequential GUID for a given instant. Exposed for deterministic tests.</summary>
    public static Guid Create(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        Random.Shared.NextBytes(bytes);

        // Milliseconds since the Unix epoch, big-endian, in the six bytes SQL Server sorts on first.
        long milliseconds = timestamp.ToUnixTimeMilliseconds();
        bytes[10] = (byte)(milliseconds >> 40);
        bytes[11] = (byte)(milliseconds >> 32);
        bytes[12] = (byte)(milliseconds >> 24);
        bytes[13] = (byte)(milliseconds >> 16);
        bytes[14] = (byte)(milliseconds >> 8);
        bytes[15] = (byte)milliseconds;

        return new Guid(bytes);
    }
}
