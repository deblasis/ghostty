using System.ComponentModel;
using Ghostty.Core.Sponsor.Update;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// Flyout content for the update pill. Uses code-behind binding
/// (same pattern as <c>CommandPaletteControl</c>): an internal VM is
/// wired via <see cref="Bind"/>, which subscribes to PropertyChanged
/// and refreshes element properties on every transition.
/// </summary>
internal sealed partial class UpdatePopover : UserControl
{
    private UpdatePopoverViewModel? _vm;

    public UpdatePopover()
    {
        InitializeComponent();
    }

    public void Bind(UpdatePopoverViewModel vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = vm;
        SkipButton.Command = vm.SkipCommand;
        InstallButton.Command = vm.InstallAndRelaunchCommand;
        RestartButton.Command = vm.RestartNowCommand;
        RetryButton.Command = vm.RetryCommand;

        vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_vm is null) return;

        HeaderBlock.Text = HeaderFromState(_vm.State);
        BodyBlock.Text = BodyFromState(_vm.State, _vm.TargetVersion, _vm.ErrorMessage);

        SkipButton.Visibility = ShowFor(_vm.State, UpdateState.UpdateAvailable);
        InstallButton.Visibility = ShowFor(_vm.State, UpdateState.UpdateAvailable);
        RestartButton.Visibility = ShowFor(_vm.State, UpdateState.RestartPending);
        RetryButton.Visibility = ShowFor(_vm.State, UpdateState.Error);
    }

    private static string HeaderFromState(UpdateState s) => s switch
    {
        UpdateState.UpdateAvailable => "Update Available",
        UpdateState.RestartPending => "Restart to Finish Updating",
        UpdateState.Error => "Update Error",
        _ => string.Empty,
    };

    private static string BodyFromState(UpdateState s, string? v, string? err) => s switch
    {
        UpdateState.UpdateAvailable => $"Version {v ?? "?"} is ready to install.",
        UpdateState.RestartPending => "Restart to apply the downloaded update.",
        UpdateState.Error => err ?? "Something went wrong checking for updates.",
        _ => string.Empty,
    };

    private static Visibility ShowFor(UpdateState current, UpdateState expected) =>
        current == expected ? Visibility.Visible : Visibility.Collapsed;
}
