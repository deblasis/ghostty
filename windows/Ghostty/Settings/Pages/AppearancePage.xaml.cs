using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ghostty.Core.Config;
using Ghostty.Core.DirectWrite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Settings.Pages;

internal sealed partial class AppearancePage : Page
{
    private readonly IConfigService _configService;
    private readonly IConfigFileEditor _editor;
    private readonly SearchableList _fontList;
    private bool _loading = true;

    public AppearancePage(IConfigService configService, IConfigFileEditor editor)
    {
        _configService = configService;
        _editor = editor;
        InitializeComponent();
        _fontList = new SearchableList(FontFamilySearch, chosen => OnValueChanged("font-family", chosen));
        OpacitySlider.Value = configService.BackgroundOpacity;
        SelectWindowTheme(configService.WindowTheme);
        _loading = false;
        LoadFontsAsync();
    }

    private void SelectWindowTheme(string theme)
    {
        foreach (ComboBoxItem item in WindowThemeCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), theme, StringComparison.OrdinalIgnoreCase))
            {
                WindowThemeCombo.SelectedItem = item;
                return;
            }
        }
        // Default to "auto" if the value is unrecognized.
        WindowThemeCombo.SelectedIndex = 0;
    }

    private void LoadFontsAsync()
    {
        FontFamilySearch.PlaceholderText = "Loading fonts...";
        var dispatcher = DispatcherQueue;
        Task.Run(() =>
        {
            var fonts = EnumerateSystemFonts();
            dispatcher.TryEnqueue(() =>
            {
                _fontList.SetItems(fonts);
                FontFamilySearch.PlaceholderText = $"Search {fonts.Count} fonts...";
            });
        });
    }

    // Thin adapter delegating to the shared Ghostty.Core helper.
    // Keeps JetBrains Mono injection at this layer because the
    // embedded font list is a Ghostty UI decision, not a DWrite
    // enumeration detail. The DWrite vtable dispatch lives in
    // Ghostty.Core.DirectWrite.DWriteFontEnumerator and is covered
    // by DWriteFontFamilyEquivalenceTest.
    private static List<string> EnumerateSystemFonts()
    {
        var families = DWriteFontEnumerator.EnumerateMigrated();

        // Ghostty embeds JetBrains Mono in the binary so it's always
        // available even if not installed on the system.
        if (!families.Contains("JetBrains Mono", StringComparer.OrdinalIgnoreCase))
        {
            families.Add("JetBrains Mono");
            families.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return families;
    }

    private void OnValueChanged(string key, string value)
    {
        if (_loading) return;
        _configService.SuppressWatcher(true);
        _editor.SetValue(key, value);
        _configService.SuppressWatcher(false);
        _configService.Reload();
    }

    private void FontSize_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        OnValueChanged("font-size", sender.Value.ToString());
    }

    private void Opacity_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        OnValueChanged("background-opacity", e.NewValue.ToString("F2"));
    }

    private void WindowTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            OnValueChanged("window-theme", item.Tag?.ToString() ?? "auto");
    }

    private void ShaderPath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) OnValueChanged("custom-shader", tb.Text);
    }
}
