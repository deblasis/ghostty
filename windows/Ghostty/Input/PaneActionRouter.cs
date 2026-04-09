using System;
using Ghostty.Core.Panes;
using Ghostty.Core.Tabs;
using Ghostty.Panes;

namespace Ghostty.Input;

/// <summary>
/// Dispatches a <see cref="PaneAction"/> against a target
/// <see cref="TabManager"/>. Pane actions are routed to the active
/// tab's <see cref="IPaneHost"/>; tab actions are routed to the
/// manager directly. Single switch lives here so that adding a new
/// action is one place to edit.
///
/// CloseActiveProgressive is special: when a pane-only close suffices
/// it goes directly to <see cref="IPaneHost.CloseActive"/>; when a
/// full-tab close is needed, the router raises
/// <see cref="TabCloseRequestedFromKeyboard"/> so MainWindow can show
/// the multi-pane confirmation dialog from a context with an XamlRoot.
/// </summary>
internal static class PaneActionRouter
{
    public static void Invoke(PaneAction action, TabManager tabs)
    {
        var pane = tabs.ActiveTab.PaneHost;
        var concrete = (PaneHost)pane;
        switch (action)
        {
            // Panes
            case PaneAction.SplitVertical:   concrete.Split(PaneOrientation.Vertical); break;
            case PaneAction.SplitHorizontal: concrete.Split(PaneOrientation.Horizontal); break;
            case PaneAction.ClosePane:       pane.CloseActive(); break;
            case PaneAction.FocusLeft:       concrete.FocusDirection(FocusDirection.Left); break;
            case PaneAction.FocusRight:      concrete.FocusDirection(FocusDirection.Right); break;
            case PaneAction.FocusUp:         concrete.FocusDirection(FocusDirection.Up); break;
            case PaneAction.FocusDown:       concrete.FocusDirection(FocusDirection.Down); break;

            // Tabs
            case PaneAction.NewTab: tabs.NewTab(); break;
            case PaneAction.CloseActiveProgressive: HandleProgressiveClose(tabs); break;
            case PaneAction.NextTab: tabs.Next(); break;
            case PaneAction.PrevTab: tabs.Prev(); break;
            case PaneAction.JumpTab1: tabs.JumpTo(0); break;
            case PaneAction.JumpTab2: tabs.JumpTo(1); break;
            case PaneAction.JumpTab3: tabs.JumpTo(2); break;
            case PaneAction.JumpTab4: tabs.JumpTo(3); break;
            case PaneAction.JumpTab5: tabs.JumpTo(4); break;
            case PaneAction.JumpTab6: tabs.JumpTo(5); break;
            case PaneAction.JumpTab7: tabs.JumpTo(6); break;
            case PaneAction.JumpTab8: tabs.JumpTo(7); break;
            case PaneAction.JumpTabLast: tabs.JumpToLast(); break;
            case PaneAction.MoveTabRight:
            {
                var i = tabs.IndexOf(tabs.ActiveTab);
                if (i >= 0 && i < tabs.Tabs.Count - 1) tabs.Move(i, i + 1);
                break;
            }
            case PaneAction.MoveTabLeft:
            {
                var i = tabs.IndexOf(tabs.ActiveTab);
                if (i > 0) tabs.Move(i, i - 1);
                break;
            }
            case PaneAction.ToggleVerticalTabsPinned:
                // Route to the ITabHost via an event so the router
                // stays free of direct ITabHost dependencies.
                // MainWindow listens and calls TogglePinnedFromKeyboard
                // on the VerticalTabHost if that's the active layout;
                // horizontal layout ignores it.
                ToggleVerticalTabsPinnedFromKeyboard?.Invoke(null, tabs);
                break;
            case PaneAction.ToggleTabLayout:
                // Runtime switch between horizontal and vertical tabs.
                // MainWindow listens and calls ToggleTabLayout which
                // animates the transition and persists the choice.
                ToggleTabLayoutFromKeyboard?.Invoke(null, tabs);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>
    /// Raised when the Ctrl+Shift+Space chord fires. MainWindow
    /// listens and calls <c>VerticalTabHost.TogglePinnedFromKeyboard</c>
    /// if the current <see cref="ITabHost"/> is a VerticalTabHost.
    /// </summary>
    public static event EventHandler<TabManager>? ToggleVerticalTabsPinnedFromKeyboard;

    /// <summary>
    /// Raised when the Ctrl+Shift+Alt+V chord fires. MainWindow
    /// listens and calls its own ToggleTabLayout which flips the
    /// active tab host between horizontal and vertical with an
    /// animated transition.
    /// </summary>
    public static event EventHandler<TabManager>? ToggleTabLayoutFromKeyboard;

    /// <summary>
    /// Public dispatch entry used by non-keyboard triggers (context
    /// menu, title-bar button). Reuses the same event path so
    /// MainWindow has a single handler for every toggle source.
    /// </summary>
    public static void RequestToggleTabLayout(TabManager tabs)
        => ToggleTabLayoutFromKeyboard?.Invoke(null, tabs);

    private static void HandleProgressiveClose(TabManager tabs)
    {
        // If the active tab has more than one pane, close one and stop.
        // Otherwise the entire tab is being closed; emit the request
        // event so MainWindow can show the confirmation dialog
        // (TabManager has no XamlRoot).
        if (tabs.ActiveTab.PaneHost.PaneCount > 1)
        {
            tabs.ActiveTab.PaneHost.CloseActive();
            return;
        }
        TabCloseRequestedFromKeyboard?.Invoke(null, tabs);
    }

    /// <summary>
    /// Raised when the keyboard close chord targets a full-tab close.
    /// MainWindow listens and shows the confirmation dialog (if needed)
    /// before calling <see cref="TabManager.CloseTab"/>. The event lives
    /// here so the router stays free of WinUI dialog dependencies.
    /// </summary>
    public static event EventHandler<TabManager>? TabCloseRequestedFromKeyboard;
}
