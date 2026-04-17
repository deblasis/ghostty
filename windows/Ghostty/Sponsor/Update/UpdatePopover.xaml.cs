using System.ComponentModel;
using System.Windows.Input;
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
        // Click+CanExecuteChanged instead of Button.Command -- WinUI 3 +
        // CsWinRT CCW tables aren't emitted for managed ICommand impls
        // assigned in code-behind. See UpdatePill.WireCommand.
        WireCommand(SkipButton,    vm.SkipCommand);
        WireCommand(InstallButton, vm.InstallAndRelaunchCommand);
        WireCommand(RestartButton, vm.RestartNowCommand);
        WireCommand(RetryButton,   vm.RetryCommand);

        vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    private static void WireCommand(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button, ICommand command)
    {
        button.Click += (_, _) =>
        {
            if (command.CanExecute(null)) command.Execute(null);
        };
        button.IsEnabled = command.CanExecute(null);
        command.CanExecuteChanged += (_, _) => button.IsEnabled = command.CanExecute(null);
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
        UpdateState.NoUpdatesFound => "Up to date",
        UpdateState.Downloading => "Downloading update",
        UpdateState.Extracting => "Preparing update",
        UpdateState.Installing => "Installing update",
        _ => string.Empty,
    };

    private static string BodyFromState(UpdateState s, string? v, string? err) => s switch
    {
        UpdateState.UpdateAvailable => $"Version {v ?? "?"} is ready to install.",
        UpdateState.RestartPending => "Restart to apply the downloaded update.",
        UpdateState.Error => FriendlyError(err),
        UpdateState.NoUpdatesFound => "You are running the latest version of wintty.",
        UpdateState.Downloading => "Retrieving the update package from the release server.",
        UpdateState.Extracting => "Verifying and extracting the downloaded package.",
        UpdateState.Installing => "Applying the update to the on-disk install. This usually takes a few seconds.",
        _ => string.Empty,
    };

    // Lead with a plain-language sentence. Driver-supplied detail (network
    // code, HTTP status, simulator label) appears on a second line so the
    // user still has something to paste into a bug report without having
    // to read it first. Empty err just shows the friendly line.
    private static string FriendlyError(string? err)
    {
        const string friendly = "We couldn't finish downloading the update. Check your internet connection and try Retry.";
        return string.IsNullOrWhiteSpace(err) ? friendly : $"{friendly}\n\nDetails: {err}";
    }

    private static Visibility ShowFor(UpdateState current, UpdateState expected) =>
        current == expected ? Visibility.Visible : Visibility.Collapsed;
}
