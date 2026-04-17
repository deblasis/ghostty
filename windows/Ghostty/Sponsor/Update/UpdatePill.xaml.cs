using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// Title-bar pill control for update status. Uses code-behind binding
/// (same pattern as <c>CommandPaletteControl</c> / <c>UpdatePopover</c>):
/// internal VM wired via <see cref="Bind"/>, which subscribes to
/// PropertyChanged and refreshes element properties on each transition.
/// The child <c>UpdatePopover</c> receives its own VM through its
/// own <c>Bind</c> method.
/// </summary>
internal sealed partial class UpdatePill : UserControl
{
    private UpdatePillViewModel? _vm;

    public UpdatePill()
    {
        InitializeComponent();
    }

    public void Bind(UpdatePillViewModel pillVm, UpdatePopoverViewModel popoverVm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = pillVm;
        PillButton.Command = pillVm.TogglePopoverCommand;
        PillPopover.Bind(popoverVm);

        pillVm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_vm is null) return;

        Visibility = _vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        PillLabel.Text = _vm.Label;
        PillIcon.Glyph = _vm.IconGlyph;
        PillIcon.Visibility = _vm.ShowProgressRing ? Visibility.Collapsed : Visibility.Visible;
        PillProgress.Visibility = _vm.ShowProgressRing ? Visibility.Visible : Visibility.Collapsed;
        PillProgress.Value = _vm.ProgressValue;
    }
}
