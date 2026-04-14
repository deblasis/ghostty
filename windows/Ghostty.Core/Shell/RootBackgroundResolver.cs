namespace Ghostty.Core.Shell;

/// <summary>
/// Pure resolver for the color that should be painted as
/// RootGrid.Background on the main window. The value is a function
/// of the active backdrop style and shell-theme state; keeping it
/// pure (no WinUI types, no side effects) lets us unit-test the
/// decision matrix in Ghostty.Tests without spinning up a XAML host.
/// </summary>
public static class RootBackgroundResolver
{
    /// <summary>ARGB for "fully transparent, let the SystemBackdrop show through".</summary>
    public const uint TransparentArgb = 0x00000000u;

    /// <summary>ARGB for the default opaque chrome color used when no shell theme is active.</summary>
    public const uint OpaqueChromeArgb = 0xFF0C0C0Cu;

    /// <summary>
    /// Resolve the ARGB color to paint as RootGrid.Background.
    /// </summary>
    /// <param name="backdropStyle">Current SystemBackdrop style ("frosted", "crystal", "solid", or "").</param>
    /// <param name="shellThemeEnabled">True when window-theme=ghostty and chrome is driven by the terminal palette.</param>
    /// <param name="shellThemeBgArgb">ARGB color to use for the shell-theme-enabled case (typically the title bar background).</param>
    /// <remarks>
    /// Truth table:
    /// <code>
    ///                         shellThemeEnabled  shellThemeEnabled
    ///                              = false            = true
    /// backdropStyle = frosted:    transparent        transparent
    /// backdropStyle = crystal:    transparent        transparent
    /// backdropStyle = solid/""/*: 0xFF0C0C0C         shellThemeBgArgb
    /// </code>
    /// Transparent backdrops always stay transparent so the acrylic
    /// or Direct Composition backdrop behind RootGrid remains visible.
    /// </remarks>
    public static uint Resolve(string backdropStyle, bool shellThemeEnabled, uint shellThemeBgArgb)
    {
        if (backdropStyle is "frosted" or "crystal")
            return TransparentArgb;
        return shellThemeEnabled ? shellThemeBgArgb : OpaqueChromeArgb;
    }
}
