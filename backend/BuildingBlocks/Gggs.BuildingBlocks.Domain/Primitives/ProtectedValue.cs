namespace Gggs.BuildingBlocks.Domain.Primitives;

/// <summary>
/// A sensitive value as it is stored: encrypted, with a short hint that is safe to show.
/// <para>
/// PAN, UAN, ESIC and bank account numbers are held like this (spec §14, §132). The ciphertext is
/// only ever turned back into the real value for a caller holding the field's permission; everyone
/// else sees <see cref="Hint"/> - the last few characters behind a mask - which is enough to tell
/// two records apart without being enough to misuse either.
/// </para>
/// <para>
/// The domain never encrypts or decrypts: it receives a <see cref="ProtectedValue"/> from the
/// application layer, which owns the key material, and stores it as given.
/// </para>
/// </summary>
public sealed record ProtectedValue
{
    public ProtectedValue(string ciphertext, string hint)
    {
        Ciphertext = Guard.NotNullOrWhiteSpace(ciphertext);
        Hint = Guard.MaxLength(Guard.NotNullOrWhiteSpace(hint), 64);
    }

    public string Ciphertext { get; }

    /// <summary>The masked form, e.g. <c>XXXXXX234F</c>.</summary>
    public string Hint { get; }

    /// <summary>Builds the masked form of a plain value, keeping the last <paramref name="visible"/> characters.</summary>
    public static string MaskOf(string plain, int visible = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plain);
        ArgumentOutOfRangeException.ThrowIfNegative(visible);

        string trimmed = plain.Trim();
        int shown = Math.Min(visible, Math.Max(0, trimmed.Length - 2));
        return new string('X', trimmed.Length - shown) + trimmed[^shown..];
    }
}
