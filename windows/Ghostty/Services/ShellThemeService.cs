using System;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace Ghostty.Services;

/// <summary>
/// Derives a UI color scheme from the terminal's ANSI palette.
/// Fires <see cref="ThemeChanged"/> when the derived colors change
/// so UI controls can update.
///
/// Shell theme updates are debounced so rapid theme browsing (e.g.
/// scrolling in the theme picker) doesn't flood the XAML layout
/// engine. Terminal rendering is unaffected -- only the tab/title
/// bar repaint is throttled.
/// </summary>
internal sealed class ShellThemeService
{
    private readonly ConfigService _configService;
    private readonly DispatcherQueue? _dispatcher;

    public event Action? ThemeChanged;

    public Windows.UI.Color TitleBarBackground { get; private set; }
    public Windows.UI.Color TitleBarForeground { get; private set; }
    public Windows.UI.Color TabBarBackground { get; private set; }
    public Windows.UI.Color AccentColor { get; private set; }
    public Windows.UI.Color InactiveTabText { get; private set; }
    public Windows.UI.Color ScrollbarTrack { get; private set; }
    public Windows.UI.Color ScrollbarThumb { get; private set; }

    public bool IsEnabled => _configService.ShellThemeEnabled;

    private bool _wasEnabled;
    private int _debouncePending;
    // Debounce interval for shell theme updates (ms). Terminal rendering
    // stays immediate; only tab/titlebar XAML repaint is throttled.
    private const int DebounceMs = 30;

    internal ShellThemeService(ConfigService configService, DispatcherQueue? dispatcher = null)
    {
        _configService = configService;
        _dispatcher = dispatcher;
        _wasEnabled = IsEnabled;
        Recompute();
        configService.ConfigChanged += _ =>
        {
            if (Recompute()) ScheduleThemeChanged();
        };
    }

    private void ScheduleThemeChanged()
    {
        // If we have a dispatcher, coalesce rapid updates so the XAML
        // layout engine isn't hammered on every theme scroll. The
        // terminal VT rendering is unaffected -- only the shell chrome
        // repaint is coalesced.
        if (_dispatcher is null)
        {
            ThemeChanged?.Invoke();
            return;
        }

        // Mark that a change is pending. If one is already queued,
        // skip -- the enqueued callback will pick up the latest colors
        // since Recompute() already wrote them to our properties.
        if (Interlocked.Exchange(ref _debouncePending, 1) == 1)
            return;

        _dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _debouncePending, 0);
            ThemeChanged?.Invoke();
        });
    }

    /// <summary>
    /// Recompute derived colors from the current palette.
    /// Returns true if any color changed or the enabled state flipped.
    /// </summary>
    private bool Recompute()
    {
        var enabled = IsEnabled;
        if (!enabled)
        {
            // Report change when transitioning from enabled to disabled
            // so MainWindow can revert to standard chrome.
            var changed = _wasEnabled;
            _wasEnabled = false;
            return changed;
        }
        _wasEnabled = true;

        var bg = UnpackColor(_configService.BackgroundColor);
        var fg = UnpackColor(_configService.ForegroundColor);
        var palette = _configService.AnsiPalette;

        var newTabBar = ShiftBrightness(bg, fg, 0.05f);
        var newAccent = _configService.CursorColor.HasValue
            ? UnpackColor(_configService.CursorColor.Value)
            : FindAccent(palette);
        var newInactive = UnpackColor(palette[8]);
        var newTrack = ShiftBrightness(bg, fg, 0.08f);
        var newThumb = Windows.UI.Color.FromArgb(
            102, fg.R, fg.G, fg.B);

        // Only report a change if something actually moved.
        bool colorsChanged = !ColorsEqual(TitleBarBackground, bg)
            || !ColorsEqual(TitleBarForeground, fg)
            || !ColorsEqual(TabBarBackground, newTabBar)
            || !ColorsEqual(AccentColor, newAccent)
            || !ColorsEqual(InactiveTabText, newInactive);

        TitleBarBackground = bg;
        TitleBarForeground = fg;
        TabBarBackground = newTabBar;
        AccentColor = newAccent;
        InactiveTabText = newInactive;
        ScrollbarTrack = newTrack;
        ScrollbarThumb = newThumb;

        return colorsChanged;
    }

    private static bool ColorsEqual(Windows.UI.Color a, Windows.UI.Color b)
        => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

    /// <summary>
    /// Pick an accent color from the ANSI palette. Prefers blue
    /// (index 4 / 12) as it's the most natural UI accent. Falls
    /// back to cyan, then the most saturated non-red color.
    /// </summary>
    private static Windows.UI.Color FindAccent(uint[] palette)
    {
        // Preference order: blue, bright blue, cyan, bright cyan.
        // These make the best UI accents across light/dark themes.
        int[] preferred = [4, 12, 6, 14];
        foreach (var i in preferred)
        {
            if (i < palette.Length && GetSaturation(UnpackColor(palette[i])) > 0.15f)
                return UnpackColor(palette[i]);
        }

        // Fallback: most saturated color, skipping black/white
        // (indices 0, 7, 8, 15) and reds (1, 9) which are too
        // aggressive for UI accent.
        float maxSat = 0f;
        int bestIdx = 4;
        int[] skip = [0, 1, 7, 8, 9, 15];
        for (int i = 0; i < 16; i++)
        {
            if (Array.IndexOf(skip, i) >= 0) continue;
            var sat = GetSaturation(UnpackColor(palette[i]));
            if (sat > maxSat) { maxSat = sat; bestIdx = i; }
        }

        return UnpackColor(palette[bestIdx]);
    }

    private static float GetSaturation(Windows.UI.Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        if (max == min) return 0f;
        float l = (max + min) / 2f;
        float d = max - min;
        return l > 0.5f ? d / (2f - max - min) : d / (max + min);
    }

    /// <summary>
    /// Shift a color slightly toward another color by the given amount.
    /// Used to derive tab bar and scrollbar track from background.
    /// </summary>
    private static Windows.UI.Color ShiftBrightness(
        Windows.UI.Color from, Windows.UI.Color toward, float amount)
    {
        return Windows.UI.Color.FromArgb(0xFF,
            (byte)(from.R + (toward.R - from.R) * amount),
            (byte)(from.G + (toward.G - from.G) * amount),
            (byte)(from.B + (toward.B - from.B) * amount));
    }

    private static Windows.UI.Color UnpackColor(uint packed)
    {
        return Windows.UI.Color.FromArgb(0xFF,
            (byte)(packed >> 16),
            (byte)(packed >> 8),
            (byte)packed);
    }
}
