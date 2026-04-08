using System;
using System.Collections.Generic;
using System.Linq;
using Ghostty.Controls;
using Ghostty.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Ghostty.Panes;

/// <summary>
/// UserControl that renders a <see cref="PaneNode"/> tree as nested
/// 2-cell <see cref="Grid"/>s with <see cref="Splitter"/>s between
/// children. Owns the tree root, the active leaf pointer, and the
/// operations that mutate them (split, close, directional focus).
///
/// Stable TerminalControl instances:
///   <see cref="TerminalControl"/> instances are created once when
///   their <see cref="LeafPane"/> is created, and are reused as the
///   tree is rebuilt: only their parent Grid changes. Recreating a
///   TerminalControl would tear down its libghostty surface and lose
///   the running shell, which is not what splitting a pane should do.
///
/// Tree mutations:
///   <see cref="Split"/> and <see cref="Close"/> mutate <see cref="_root"/>
///   via <see cref="PaneTree"/> and then call <see cref="Rebuild"/>.
///   The whole subtree is rebuilt for simplicity; the trees are tiny
///   and rebuild cost is negligible compared to a libghostty frame.
///
/// Focus tracking:
///   We subscribe to each <see cref="TerminalControl.GotFocus"/> and
///   maintain <see cref="ActiveLeaf"/>. <see cref="LeafFocused"/> fires
///   when ActiveLeaf changes so MainWindow can re-route the title.
/// </summary>
internal sealed class PaneHost : UserControl
{
    private readonly GhosttyHost _host;
    private readonly Func<TerminalControl> _terminalFactory;

    private PaneNode _root;
    private LeafPane _activeLeaf;

    /// <summary>
    /// Raised when the active leaf changes (initially and on every focus
    /// change between leaves). Subscribers receive the new active leaf.
    /// </summary>
    public event EventHandler<LeafPane>? LeafFocused;

    /// <summary>
    /// Raised when the last leaf in the tree closes. Subscribers should
    /// close the window.
    /// </summary>
    public event EventHandler? LastLeafClosed;

    /// <summary>
    /// Currently focused leaf. Never null after construction; closing
    /// the last leaf raises <see cref="LastLeafClosed"/> instead of
    /// nulling this.
    /// </summary>
    public LeafPane ActiveLeaf => _activeLeaf;

    /// <param name="host">Per-window libghostty host. Passed to every
    /// <see cref="TerminalControl"/> created by this PaneHost.</param>
    /// <param name="terminalFactory">Factory that produces a fresh
    /// <see cref="TerminalControl"/> with no <see cref="TerminalControl.Host"/>
    /// set. PaneHost assigns Host before adding the control to the
    /// visual tree, ensuring the OnLoaded guard fires only once Host
    /// is in place.</param>
    public PaneHost(GhosttyHost host, Func<TerminalControl> terminalFactory)
    {
        _host = host;
        _terminalFactory = terminalFactory;

        // Initial single leaf.
        var firstTerminal = CreateTerminal();
        _activeLeaf = new LeafPane(firstTerminal);
        _root = _activeLeaf;

        Content = BuildVisual(_root);
        // Defer the first LeafFocused so subscribers (MainWindow) can
        // wire up before the event fires.
        Loaded += (_, _) => LeafFocused?.Invoke(this, _activeLeaf);
    }

    // Public operations -------------------------------------------------

    /// <summary>
    /// Split the active leaf with the given orientation. The new leaf
    /// becomes the active leaf.
    /// </summary>
    public void Split(PaneOrientation orientation)
    {
        var newTerminal = CreateTerminal();
        var newLeaf = new LeafPane(newTerminal);
        _root = PaneTree.Split(_root, _activeLeaf, newLeaf, orientation);
        _activeLeaf = newLeaf;
        Rebuild();
        // Focus moves on the next layout pass; the control is not in
        // the tree yet at this exact instant.
        DispatcherQueue.TryEnqueue(() => newTerminal.Focus(FocusState.Programmatic));
    }

    /// <summary>
    /// Close the active leaf. If it was the only leaf, raises
    /// <see cref="LastLeafClosed"/>; otherwise the sibling subtree
    /// replaces the parent split and focus moves to the sibling's
    /// first leaf.
    /// </summary>
    public void CloseActive()
    {
        CloseLeaf(_activeLeaf);
    }

    /// <summary>
    /// Close a specific leaf. Used both by the keybinding (which closes
    /// <see cref="ActiveLeaf"/>) and by <see cref="TerminalControl.CloseRequested"/>
    /// from libghostty's close-surface callback.
    /// </summary>
    public void CloseLeaf(LeafPane leaf)
    {
        // Detach the terminal from focus tracking BEFORE we drop it.
        leaf.Terminal.GotFocus -= OnTerminalGotFocus;

        var newRoot = PaneTree.Close(_root, leaf);
        if (newRoot is null)
        {
            // Last leaf - tell MainWindow to close the window. The
            // terminal's OnUnloaded will free its surface as soon as
            // the window goes away.
            LastLeafClosed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _root = newRoot;
        // Focus the first leaf of the (former) sibling subtree. We
        // pick the parent's sibling first so the focus stays close to
        // where the closed pane was.
        var nextActive = PaneTree.FirstLeaf(newRoot);
        _activeLeaf = nextActive;
        Rebuild();
        DispatcherQueue.TryEnqueue(() => nextActive.Terminal.Focus(FocusState.Programmatic));
    }

    /// <summary>
    /// Move focus to the leaf nearest the active leaf in the requested
    /// direction. Geometric (uses rendered rects), not tree-order.
    /// No-op if no leaf lies in that direction.
    /// </summary>
    public void FocusDirection(FocusDirection direction)
    {
        var allLeaves = PaneTree.Leaves(_root).ToList();
        if (allLeaves.Count <= 1) return;

        var activeRect = GetLeafRect(_activeLeaf);
        if (activeRect is null) return;

        LeafPane? best = null;
        double bestDistance = double.MaxValue;

        var ac = Center(activeRect.Value);
        foreach (var leaf in allLeaves)
        {
            if (ReferenceEquals(leaf, _activeLeaf)) continue;
            var rect = GetLeafRect(leaf);
            if (rect is null) continue;
            var c = Center(rect.Value);

            // Direction filter: candidate's center must lie strictly in
            // the requested direction from the active center, AND must
            // overlap on the perpendicular axis (so a pane two rows
            // down isn't considered "Right" of the active one just
            // because its center.X is greater).
            switch (direction)
            {
                case Panes.FocusDirection.Left:
                    if (c.X >= ac.X) continue;
                    if (rect.Value.Bottom <= activeRect.Value.Top) continue;
                    if (rect.Value.Top >= activeRect.Value.Bottom) continue;
                    break;
                case Panes.FocusDirection.Right:
                    if (c.X <= ac.X) continue;
                    if (rect.Value.Bottom <= activeRect.Value.Top) continue;
                    if (rect.Value.Top >= activeRect.Value.Bottom) continue;
                    break;
                case Panes.FocusDirection.Up:
                    if (c.Y >= ac.Y) continue;
                    if (rect.Value.Right <= activeRect.Value.Left) continue;
                    if (rect.Value.Left >= activeRect.Value.Right) continue;
                    break;
                case Panes.FocusDirection.Down:
                    if (c.Y <= ac.Y) continue;
                    if (rect.Value.Right <= activeRect.Value.Left) continue;
                    if (rect.Value.Left >= activeRect.Value.Right) continue;
                    break;
            }

            // Manhattan distance between centers.
            var dist = Math.Abs(c.X - ac.X) + Math.Abs(c.Y - ac.Y);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                best = leaf;
            }
        }

        best?.Terminal.Focus(FocusState.Keyboard);
    }

    // Internals ---------------------------------------------------------

    private TerminalControl CreateTerminal()
    {
        var t = _terminalFactory();
        // Host MUST be set before the control is loaded; TerminalControl
        // throws otherwise.
        t.Host = _host;
        t.GotFocus += OnTerminalGotFocus;
        // CloseRequested fires from libghostty's close-surface callback
        // (via GhosttyHost). Route it to the leaf-level close path so
        // multi-pane closing collapses correctly.
        t.CloseRequested += OnTerminalCloseRequested;
        return t;
    }

    private void OnTerminalGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TerminalControl tc) return;
        var leaf = PaneTree.Leaves(_root).FirstOrDefault(l => ReferenceEquals(l.Terminal, tc));
        if (leaf is null) return;
        if (ReferenceEquals(leaf, _activeLeaf)) return;
        _activeLeaf = leaf;
        LeafFocused?.Invoke(this, _activeLeaf);
    }

    private void OnTerminalCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not TerminalControl tc) return;
        var leaf = PaneTree.Leaves(_root).FirstOrDefault(l => ReferenceEquals(l.Terminal, tc));
        if (leaf is null) return;
        CloseLeaf(leaf);
    }

    private void Rebuild()
    {
        Content = BuildVisual(_root);
    }

    private FrameworkElement BuildVisual(PaneNode node)
    {
        if (node is LeafPane leaf)
        {
            // The leaf's TerminalControl is stable across rebuilds.
            // Detach it from any previous parent before re-parenting.
            DetachFromParent(leaf.Terminal);
            return leaf.Terminal;
        }

        var split = (SplitPane)node;
        var grid = new Grid();
        if (split.Orientation == PaneOrientation.Vertical)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split.Ratio, GridUnitType.Star) });

            var left = BuildVisual(split.Child1);
            Grid.SetColumn(left, 0);
            var right = BuildVisual(split.Child2);
            Grid.SetColumn(right, 1);

            grid.Children.Add(left);
            grid.Children.Add(right);

            // Splitter sits at column 0's right edge (the column boundary).
            // Pinned inside cell 0 with HorizontalAlignment.Right so it
            // rides the boundary as star weights change. No ColumnSpan.
            ApplyRatio(grid, split);
            var splitter = new Splitter(split, () => ApplyRatio(grid, split));
            Grid.SetColumn(splitter, 0);
            splitter.HorizontalAlignment = HorizontalAlignment.Right;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.Width = 1;
            grid.Children.Add(splitter);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - split.Ratio, GridUnitType.Star) });

            var top = BuildVisual(split.Child1);
            Grid.SetRow(top, 0);
            var bottom = BuildVisual(split.Child2);
            Grid.SetRow(bottom, 1);

            grid.Children.Add(top);
            grid.Children.Add(bottom);

            // Splitter sits at row 0's bottom edge (the row boundary).
            // Pinned inside cell 0 with VerticalAlignment.Bottom so it
            // rides the boundary as star weights change. No RowSpan.
            ApplyRatio(grid, split);
            var splitter = new Splitter(split, () => ApplyRatio(grid, split));
            Grid.SetRow(splitter, 0);
            splitter.VerticalAlignment = VerticalAlignment.Bottom;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.Height = 1;
            grid.Children.Add(splitter);
        }

        return grid;
    }

    /// <summary>
    /// Apply the current ratio to the grid's row/column definitions
    /// without rebuilding any children. Called both on initial build
    /// and on each splitter drag delta.
    /// </summary>
    private static void ApplyRatio(Grid grid, SplitPane split)
    {
        if (split.Orientation == PaneOrientation.Vertical)
        {
            if (grid.ColumnDefinitions.Count == 2)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(split.Ratio, GridUnitType.Star);
                grid.ColumnDefinitions[1].Width = new GridLength(1 - split.Ratio, GridUnitType.Star);
            }
        }
        else
        {
            if (grid.RowDefinitions.Count == 2)
            {
                grid.RowDefinitions[0].Height = new GridLength(split.Ratio, GridUnitType.Star);
                grid.RowDefinitions[1].Height = new GridLength(1 - split.Ratio, GridUnitType.Star);
            }
        }
    }

    private static void DetachFromParent(FrameworkElement child)
    {
        if (child.Parent is Panel parent)
        {
            parent.Children.Remove(child);
        }
    }

    private Rect? GetLeafRect(LeafPane leaf)
    {
        var ctl = leaf.Terminal;
        if (ctl.ActualWidth <= 0 || ctl.ActualHeight <= 0) return null;
        try
        {
            var transform = ctl.TransformToVisual(this);
            return transform.TransformBounds(new Rect(0, 0, ctl.ActualWidth, ctl.ActualHeight));
        }
        catch
        {
            return null;
        }
    }

    private static Point Center(Rect r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
}

/// <summary>
/// Direction for <see cref="PaneHost.FocusDirection(FocusDirection)"/>.
/// </summary>
internal enum FocusDirection
{
    Left,
    Right,
    Up,
    Down,
}
