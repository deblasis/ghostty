using Microsoft.UI.Xaml;

namespace Ghostty.Branding;

/// <summary>
/// Walks up from a XAML element to find its owning Window. Used by
/// AppIconBadge so the click handler can hand the Window to the
/// system-menu interop helper.
/// </summary>
internal static class WindowHelper
{
    public static Window? GetWindow(FrameworkElement element)
    {
        // WinUI 3 desktop does not expose Window.Current. FrameworkElement
        // does not directly surface the owning Window either. The simplest
        // reliable path is App.RootWindow, which the App class sets when
        // it creates the main window.
        if (element.XamlRoot is null) return null;
        return App.RootWindow;
    }
}
