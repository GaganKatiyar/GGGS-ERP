using System.Globalization;
using System.Text.RegularExpressions;

namespace Gggs.Notification.Application.Notifications;

/// <summary>
/// Renders a template against event data only.
/// <para>
/// architecture.md §14 calls for a sandboxed engine such as Scriban. Every version of the actual
/// Scriban package available when this was built carried multiple published high/critical
/// advisories (NU1902-NU1904 failed the restore outright), so this slice ships the plainest
/// mechanism that could not possibly reopen any of them: <c>{{ key }}</c> placeholders substituted
/// by simple lookup, with no expression language, no loops and no conditionals to have a
/// vulnerability in. A template can only ever produce the literal string value of a key the caller
/// supplied - there is no way for it to read a file, make a request, or execute anything.
/// Upgrading to a real engine, once one exists with a clean advisory history, is a renderer swap
/// behind this same signature, not a redesign.
/// </para>
/// </summary>
public static partial class NotificationTemplateRenderer
{
    public static string Render(string templateSource, IReadOnlyDictionary<string, object?> data)
        => PlaceholderPattern().Replace(templateSource, match =>
        {
            string key = match.Groups["key"].Value.Trim();
            return data.TryGetValue(key, out object? value) ? Format(value) : match.Value;
        });

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        DateOnly dateOnly => dateOnly.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    [GeneratedRegex(@"\{\{\s*(?<key>[a-zA-Z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}
