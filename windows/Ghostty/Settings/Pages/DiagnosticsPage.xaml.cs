using System.Collections.ObjectModel;
using Ghostty.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Settings.Pages;

internal sealed partial class DiagnosticsPage : Page
{
    private readonly IConfigService _configService;
    private readonly ObservableCollection<string> _diagnostics = new();

    public DiagnosticsPage(IConfigService configService)
    {
        _configService = configService;
        InitializeComponent();
        DiagList.ItemsSource = _diagnostics;
        // AOT-safe: populate TextBlock from code-behind instead of
        // {Binding} which relies on reflection that NativeAOT trims.
        DiagList.ContainerContentChanging += OnContainerContentChanging;
        _configService.ConfigChanged += OnConfigChanged;
        Unloaded += OnUnloaded;
        Refresh();
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

    private void OnConfigChanged(IConfigService _) => Refresh();

    private void Refresh()
    {
        _diagnostics.Clear();
        var count = _configService.DiagnosticsCount;
        for (int i = 0; i < count; i++)
        {
            _diagnostics.Add(_configService.GetDiagnostic(i));
        }

        CountBadge.Value = count;
        StatusText.Text = count == 0
            ? "No issues found"
            : $"{count} diagnostic(s)";
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.Reload();
    }
}
