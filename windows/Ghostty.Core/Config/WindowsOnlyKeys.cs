using System;
using System.Collections.Generic;

namespace Ghostty.Core.Config;

/// <summary>
/// Registry of config keys the Windows fork introduces that are not
/// in upstream Ghostty's Zig config schema. libghostty flags these as
/// "unknown field" during parse, but we handle them ourselves by
/// reading the raw config file; this registry lets us suppress the
/// false-positive diagnostics and surface the keys as informational
/// instead.
///
/// This is also the single place to look when we eventually propose
/// these keys upstream: each entry carries a short description and
/// a pointer to where the runtime behavior lives.
///
/// When adding a new Windows-only key:
/// 1. Add an entry here.
/// 2. Make sure ConfigService reads it via GetFileValue (not through
///    libghostty, which doesn't know about it).
/// 3. If it's user-editable from the settings UI, also add it to
///    SettingsIndex with an appropriate page/section.
/// </summary>
public static class WindowsOnlyKeys
{
    public readonly record struct Entry(string Key, string Description);

    public static readonly IReadOnlyList<Entry> All = new Entry[]
    {
        new("background-style",
            "Backdrop material preset (solid/frosted/crystal)."),
        new("background-tint-color",
            "Tint color overlaid on the acrylic backdrop."),
        new("background-tint-opacity",
            "Strength of the acrylic tint color."),
        new("background-luminosity-opacity",
            "Strength of the acrylic luminosity layer."),
        new("background-blur-follows-opacity",
            "Reduce blur radius as background-opacity increases."),
        new("background-gradient-point",
            "Position/color/radius of a radial gradient source (repeatable)."),
        new("background-gradient-animation",
            "Motion preset applied to gradient points."),
        new("background-gradient-speed",
            "Animation speed multiplier for gradient motion."),
        new("background-gradient-blend",
            "Whether the gradient renders over or under terminal text."),
        new("background-gradient-opacity",
            "Strength of the gradient tint layer."),
    };

    public static readonly IReadOnlySet<string> Set =
        new HashSet<string>(
            EnumerateKeys(All),
            StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string key) => Set.Contains(key);

    private static IEnumerable<string> EnumerateKeys(IReadOnlyList<Entry> entries)
    {
        foreach (var e in entries) yield return e.Key;
    }

    /// <summary>
    /// Extract the config key from a libghostty "unknown field"
    /// diagnostic. The precomputed message format is
    /// <c>[FILE:LINE:]KEY: unknown field</c>; the key is the last
    /// colon-separated segment before the trailing suffix. Returns
    /// empty if the message isn't an "unknown field" diagnostic.
    /// </summary>
    /// <remarks>
    /// Windows paths in the prefix contain colons (e.g. C:\Users\...),
    /// but config keys themselves never do, so splitting on the final
    /// ':' before the suffix gives the key unambiguously.
    /// </remarks>
    public static bool TryExtractUnknownFieldKey(string message, out string key)
    {
        const string Suffix = ": unknown field";
        if (message is null || !message.EndsWith(Suffix, StringComparison.Ordinal))
        {
            key = string.Empty;
            return false;
        }
        var prefix = message[..^Suffix.Length];
        var lastColon = prefix.LastIndexOf(':');
        key = lastColon >= 0 ? prefix[(lastColon + 1)..] : prefix;
        return key.Length > 0;
    }
}
