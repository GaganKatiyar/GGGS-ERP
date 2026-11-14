namespace Gggs.BuildingBlocks.Files;

/// <summary>Tells which picture format a file really is, from its own bytes rather than its name or declared type.</summary>
public static class ImageFormat
{
    /// <summary>The image type the first bytes declare (JPEG, PNG or WebP), or null for anything else.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        // RIFF....WEBP
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
