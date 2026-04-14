using System;
using System.Globalization;

namespace Ghostty.Core.Settings;

/// <summary>
/// 24-bit sRGB color used by the settings UI. Parses/formats the
/// "#RRGGBB" hex strings that libghostty config accepts, and converts
/// to/from <see cref="Hsv"/> for the ColorPickerControl.
/// </summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static bool TryParseHex(string value, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var s = value.Trim();
        if (s.StartsWith('#')) s = s[1..];

        if (s.Length == 3)
        {
            // Expand "abc" -> "aabbcc" (standard CSS short form).
            Span<char> expanded = stackalloc char[6];
            expanded[0] = s[0]; expanded[1] = s[0];
            expanded[2] = s[1]; expanded[3] = s[1];
            expanded[4] = s[2]; expanded[5] = s[2];
            s = new string(expanded);
        }

        if (s.Length != 6) return false;

        if (!byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;

        rgb = new Rgb(r, g, b);
        return true;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Unpack a 24-bit RGB value packed as R in the high byte and B in
    /// the low byte (the layout used by <c>ConfigService</c>'s color
    /// properties).
    /// </summary>
    public static Rgb FromRgb24(uint packed)
        => new((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));

    public Hsv ToHsv()
    {
        var r = R / 255.0;
        var g = G / 255.0;
        var b = B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h;
        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            var segment = (g - b) / delta;
            h = 60 * (segment < 0 ? segment + 6 : segment);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / delta + 2);
        }
        else
        {
            h = 60 * ((r - g) / delta + 4);
        }

        var s = max == 0 ? 0 : delta / max;
        return new Hsv(h, s, max);
    }

    public static Rgb FromHsv(Hsv hsv)
    {
        var h = hsv.H % 360;
        if (h < 0) h += 360;
        var s = Math.Clamp(hsv.S, 0, 1);
        var v = Math.Clamp(hsv.V, 0, 1);

        var c = v * s;
        var hp = h / 60.0;
        var x = c * (1 - Math.Abs((hp % 2) - 1));
        var m = v - c;

        double r1, g1, b1;
        switch ((int)hp)
        {
            case 0: r1 = c; g1 = x; b1 = 0; break;
            case 1: r1 = x; g1 = c; b1 = 0; break;
            case 2: r1 = 0; g1 = c; b1 = x; break;
            case 3: r1 = 0; g1 = x; b1 = c; break;
            case 4: r1 = x; g1 = 0; b1 = c; break;
            default: r1 = c; g1 = 0; b1 = x; break; // hp in [5, 6)
        }

        return new Rgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }
}
