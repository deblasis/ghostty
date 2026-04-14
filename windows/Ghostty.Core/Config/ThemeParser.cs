using System;
using System.Collections.Generic;

namespace Ghostty.Core.Config;

/// <summary>
/// Pure-logic parsing for ghostty theme configuration values.
/// Mirrors the Zig parser in Config.zig (Theme.parseCLI) for the
/// conditional theme syntax, and parses palette entries from theme
/// files for the C# side that needs them for UI accents.
/// </summary>
public static class ThemeParser
{
    /// <summary>
    /// Parse a theme config value into light/dark components.
    /// Handles single ("ThemeName") and pair ("light:X,dark:Y") forms.
    /// Returns (null, null) for single themes or empty input. Both
    /// light and dark must be present for a pair; partial pairs (only
    /// light: or only dark:) return (null, null) to match the Zig
    /// parser which treats those as InvalidValue errors.
    /// </summary>
    public static (string? light, string? dark) ParseThemePair(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
            return (null, null);

        var parts = theme.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? light = null;
        string? dark = null;

        foreach (var part in parts)
        {
            // Find the first ':' or '='. Match Zig which tolerates spaces
            // around the separator (e.g. "dark : bar").
            var sepIdx = part.IndexOfAny([':', '=']);
            if (sepIdx <= 0) continue;

            var name = part[..sepIdx].Trim();
            var value = part[(sepIdx + 1)..].Trim();
            if (value.Length == 0) continue;

            if (string.Equals(name, "light", StringComparison.OrdinalIgnoreCase))
                light = value;
            else if (string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase))
                dark = value;
        }

        // Zig's Theme.parseCLI requires both light and dark to be present;
        // a partial pair (e.g. "light:X" alone) is an error. Match that here.
        if (light is not null && dark is not null)
            return (light, dark);

        return (null, null);
    }

    /// <summary>
    /// Parse "palette = N=#RRGGBB" lines and write into the supplied
    /// 16-entry palette array. Lines that aren't palette entries are
    /// ignored, so this can be pointed at a full theme file or the
    /// user's main config file.
    /// </summary>
    public static void ApplyPaletteFromLines(IEnumerable<string> lines, uint[] palette)
    {
        if (palette.Length < 16)
            throw new ArgumentException("palette must have at least 16 entries", nameof(palette));

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0) continue;
            var key = trimmed[..eqIndex].Trim();
            if (key != "palette") continue;

            var entry = trimmed[(eqIndex + 1)..].Trim();
            var entryEq = entry.IndexOf('=');
            if (entryEq < 0) continue;
            var idxStr = entry[..entryEq].Trim();
            if (!int.TryParse(idxStr, out var parsedIdx)) continue;
            if (parsedIdx is < 0 or >= 16) continue;

            var colorStr = entry[(entryEq + 1)..].Trim();
            if (TryParseHexRgb(colorStr, out var packed))
                palette[parsedIdx] = packed;
        }
    }

    /// <summary>
    /// Parse a hex color string (#RGB, #RRGGBB, or #AARRGGBB) into
    /// a packed 0x00RRGGBB uint. Returns false on invalid input.
    /// </summary>
    public static bool TryParseHexRgb(string value, out uint packed)
    {
        packed = 0;
        if (string.IsNullOrEmpty(value)) return false;
        var hex = value.TrimStart('#');
        try
        {
            switch (hex.Length)
            {
                case 3:
                    packed = ((uint)byte.Parse(new string(hex[0], 2), System.Globalization.NumberStyles.HexNumber) << 16)
                           | ((uint)byte.Parse(new string(hex[1], 2), System.Globalization.NumberStyles.HexNumber) << 8)
                           | byte.Parse(new string(hex[2], 2), System.Globalization.NumberStyles.HexNumber);
                    return true;
                case 6:
                    packed = ((uint)byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber) << 16)
                           | ((uint)byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) << 8)
                           | byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                    return true;
                case 8:
                    // #AARRGGBB - drop the alpha
                    packed = ((uint)byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) << 16)
                           | ((uint)byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber) << 8)
                           | byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber);
                    return true;
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
