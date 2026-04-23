using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ghostty.Core.Config;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.UI;

namespace Ghostty.Settings.Pages;

internal sealed partial class RawEditorPage : Page
{
    private readonly IConfigService _configService;
    private readonly IConfigFileEditor _editor;
    private readonly ObservableCollection<string> _diagnostics = new();
    private int _lastDiagnosticCount = -1;

    private string _lastLoadedText = string.Empty;

    // Find state
    private readonly List<int> _findMatches = new();
    private int _findCurrentIndex = -1;

    // Track highlighted ranges so we only clear those (never the full document).
    private readonly List<(int start, int end)> _prevRanges = new();
    private (int start, int end) _prevCurrentRange;

    // Reusable colors to avoid allocating on every keystroke.
    private Color _matchColor;
    private Color _currentColor;
    private bool _colorsReady;

    public RawEditorPage(IConfigService configService, IConfigFileEditor editor)
    {
        _configService = configService;
        _editor = editor;
        InitializeComponent();

        DiagList.ItemsSource = _diagnostics;
        DiagList.ContainerContentChanging += OnContainerContentChanging;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        LoadContent();
        RefreshDiagnostics(isInitialLoad: true);
    }

    private string GetEditorText()
    {
        Editor.Document.GetText(TextGetOptions.None, out var text);
        return text;
    }

    private void SetEditorText(string text)
    {
        Editor.Document.SetText(TextSetOptions.None, text);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _configService.ConfigChanged += OnConfigChanged;
        RefreshFromDiskIfPristine();
        RefreshDiagnostics(isInitialLoad: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _configService.ConfigChanged -= OnConfigChanged;
    }

    private static void OnContainerContentChanging(
        ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.ItemContainer.ContentTemplateRoot is TextBlock tb
            && args.Item is string text)
        {
            tb.Text = text;
        }
    }

    private void OnConfigChanged(IConfigService _)
    {
        RefreshDiagnostics(isInitialLoad: false);
        RefreshFromDiskIfPristine();
    }

    private void RefreshFromDiskIfPristine()
    {
        if (GetEditorText() == _lastLoadedText)
            LoadContent();
    }

    private void LoadContent()
    {
        SetEditorText(_editor.ReadAll());
        _lastLoadedText = GetEditorText();
        StatusText.Text = $"Loaded from {_configService.ConfigFilePath}";
    }

    private void RefreshDiagnostics(bool isInitialLoad)
    {
        var count = _configService.DiagnosticsCount;
        var items = new List<string>(count);
        for (int i = 0; i < count; i++)
            items.Add(_configService.GetDiagnostic(i));

        _diagnostics.Clear();
        foreach (var item in items)
            _diagnostics.Add(item);

        var hasErrors = count > 0;
        StatusOkIcon.Visibility = hasErrors ? Visibility.Collapsed : Visibility.Visible;
        StatusErrorIcon.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = hasErrors
            ? (count == 1 ? "1 issue" : $"{count} issues")
            : "No issues";
        CountBadge.Value = count;
        CountBadge.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;

        DiagList.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;
        DiagEmptyState.Visibility = hasErrors ? Visibility.Collapsed : Visibility.Visible;

        var wasClean = _lastDiagnosticCount == 0;
        if (count == 0)
            DiagnosticsExpander.IsExpanded = false;
        else if (isInitialLoad || wasClean)
            DiagnosticsExpander.IsExpanded = true;

        _lastDiagnosticCount = count;
        RefreshWindowsOnlyInfo();
    }

    private void RefreshWindowsOnlyInfo()
    {
        var keys = _configService.WindowsOnlyKeysUsed;
        var count = keys.Count;

        if (count == 0)
        {
            WindowsOnlyExpander.Visibility = Visibility.Collapsed;
            WindowsOnlyExpander.IsExpanded = false;
            WindowsOnlyContentSlot.Content = null;
            return;
        }

        WindowsOnlyCountBadge.Value = count;
        WindowsOnlyContentSlot.Content = BuildWindowsOnlyContent(keys);
        WindowsOnlyExpander.Visibility = Visibility.Visible;
    }

    private RichTextBlock BuildWindowsOnlyContent(IReadOnlyList<string> keys)
    {
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        var para = new Paragraph();
        para.Inlines.Add(new Run { Text = "You're using " });
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) para.Inlines.Add(new Run { Text = ", " });
            para.Inlines.Add(BuildCodePill(keys[i]));
        }
        para.Inlines.Add(new Run
        {
            Text = ". These are Windows-only settings and may be ignored "
                 + "on other operating systems if you share this config.",
        });
        rtb.Blocks.Add(para);
        return rtb;
    }

    private InlineUIContainer BuildCodePill(string key)
    {
        var border = new Border
        {
            Style = (Style)Resources["CodePillBorderStyle"],
            Child = new TextBlock
            {
                Text = key,
                Style = (Style)Resources["CodePillTextStyle"],
            },
        };
        if (WindowsOnlyKeys.ByKey.TryGetValue(key, out var entry))
            ToolTipService.SetToolTip(border, entry.Description);
        return new InlineUIContainer { Child = border };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.SuppressWatcher(true);
        _editor.WriteRaw(GetEditorText());
        _lastLoadedText = GetEditorText();
        _configService.SuppressWatcher(false);

        var success = _configService.Reload();
        var count = _configService.DiagnosticsCount;
        StatusText.Text = success
            ? $"Saved and reloaded ({count} diagnostic{(count == 1 ? "" : "s")})"
            : "Reload failed -- check diagnostics";
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        LoadContent();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.Reload();
        LoadContent();
    }

    // --- Find bar ---

    private void EnsureColors()
    {
        if (_colorsReady) return;
        var isDark = Editor.ActualTheme == ElementTheme.Dark;
        _matchColor = isDark
            ? Color.FromArgb(70, 200, 170, 50)
            : Color.FromArgb(90, 255, 230, 100);
        _currentColor = isDark
            ? Color.FromArgb(180, 255, 200, 60)
            : Color.FromArgb(220, 255, 190, 50);
        _colorsReady = true;
    }

    private void ClearPreviousHighlights()
    {
        foreach (var (s, e) in _prevRanges)
        {
            var r = Editor.Document.GetRange(s, e);
            r.CharacterFormat.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
        }
        if (_prevCurrentRange != default)
        {
            var cr = Editor.Document.GetRange(_prevCurrentRange.start, _prevCurrentRange.end);
            cr.CharacterFormat.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
        }
        _prevRanges.Clear();
        _prevCurrentRange = default;
    }

    private void CtrlF_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FindBar.Visibility == Visibility.Visible)
        {
            FindInput.SelectAll();
            FindInput.Focus(FocusState.Keyboard);
        }
        else
        {
            ShowFindBar();
        }
        args.Handled = true;
    }

    private void F3_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FindBar.Visibility == Visibility.Visible && _findMatches.Count > 0)
            FindNextMatch();
        args.Handled = true;
    }

    private void ShiftF3_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FindBar.Visibility == Visibility.Visible && _findMatches.Count > 0)
            FindPrevMatch();
        args.Handled = true;
    }

    private void ShowFindBar()
    {
        FindBar.Visibility = Visibility.Visible;
        var sel = Editor.Document.Selection;
        if (!string.IsNullOrEmpty(sel.Text))
            FindInput.Text = sel.Text;
        else
            FindInput.Text = string.Empty;
        FindInput.SelectAll();
        FindInput.Focus(FocusState.Keyboard);
        UpdateFindMatches();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ClearPreviousHighlights();
        _findMatches.Clear();
        _findCurrentIndex = -1;
        FindMatchCount.Text = string.Empty;
        Editor.Focus(FocusState.Programmatic);
    }

    private void FindInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFindMatches();
    }

    private void FindInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideFindBar();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Shift);
            if (shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                FindPrevMatch();
            else
                FindNextMatch();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNextMatch();
    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindPrevMatch();
    private void FindClose_Click(object sender, RoutedEventArgs e) => HideFindBar();

    private void FindCaseSensitive_Click(object sender, RoutedEventArgs e)
    {
        UpdateFindMatches();
    }

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape
            && FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
            e.Handled = true;
        }
    }

    // --- Find logic ---

    private void UpdateFindMatches()
    {
        var query = FindInput.Text;
        _findMatches.Clear();
        _findCurrentIndex = -1;

        if (string.IsNullOrEmpty(query))
        {
            FindMatchCount.Text = string.Empty;
            ClearPreviousHighlights();
            return;
        }

        var text = GetEditorText();
        var comparison = FindCaseSensitive.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int pos = 0;
        while (pos <= text.Length - query.Length)
        {
            var idx = text.IndexOf(query, pos, comparison);
            if (idx < 0) break;
            _findMatches.Add(idx);
            pos = idx + query.Length;
        }

        if (_findMatches.Count == 0)
        {
            FindMatchCount.Text = "No results";
            ClearPreviousHighlights();
            return;
        }

        _findCurrentIndex = 0;
        ApplyHighlights();
        NavigateToMatch(_findCurrentIndex);
        UpdateMatchCountDisplay();
    }

    private void FindNextMatch()
    {
        if (_findMatches.Count == 0) return;
        _findCurrentIndex = (_findCurrentIndex + 1) % _findMatches.Count;
        ApplyHighlights();
        NavigateToMatch(_findCurrentIndex);
        UpdateMatchCountDisplay();
    }

    private void FindPrevMatch()
    {
        if (_findMatches.Count == 0) return;
        _findCurrentIndex = (_findCurrentIndex - 1 + _findMatches.Count) % _findMatches.Count;
        ApplyHighlights();
        NavigateToMatch(_findCurrentIndex);
        UpdateMatchCountDisplay();
    }

    private void NavigateToMatch(int index)
    {
        var start = _findMatches[index];
        var end = start + FindInput.Text.Length;
        var range = Editor.Document.GetRange(start, end);
        range.ScrollIntoView(PointOptions.Start);
        Editor.Document.Selection.SetRange(start, end);
    }

    private void ApplyHighlights()
    {
        EnsureColors();
        ClearPreviousHighlights();

        var query = FindInput.Text;
        var queryLen = query.Length;
        var limit = Math.Min(_findMatches.Count, 50);

        for (int i = 0; i < limit; i++)
        {
            var s = _findMatches[i];
            var e = s + queryLen;
            var range = Editor.Document.GetRange(s, e);
            range.CharacterFormat.BackgroundColor = _matchColor;
            _prevRanges.Add((s, e));
        }

        // Current match gets brighter highlight.
        var cs = _findMatches[_findCurrentIndex];
        var ce = cs + queryLen;
        var curRange = Editor.Document.GetRange(cs, ce);
        curRange.CharacterFormat.BackgroundColor = _currentColor;
        _prevCurrentRange = (cs, ce);
    }

    private void UpdateMatchCountDisplay()
    {
        FindMatchCount.Text = $"{_findCurrentIndex + 1} of {_findMatches.Count}";
    }
}
