using System;
using Ghostty.Core.Sponsor.Update;
using Ghostty.Mvvm;
using Microsoft.UI.Dispatching;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// ViewModel for <c>UpdatePill</c>. Subscribes to
/// <see cref="UpdateService.StateChanged"/>, marshals to the UI thread,
/// and re-projects via <see cref="PillDisplayModel.MapFromState"/>.
/// </summary>
internal sealed class UpdatePillViewModel : NotifyBase, IDisposable
{
    private readonly UpdateService _service;
    private readonly DispatcherQueue _dispatcher;

    public UpdatePillViewModel(UpdateService service, DispatcherQueue dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        TogglePopoverCommand = new RelayCommand(() => IsPopoverOpen = !IsPopoverOpen);
        _service.StateChanged += OnStateChanged;
        Project(_service.Current);
    }

    public bool IsVisible
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    public string Label
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    } = string.Empty;

    public string IconGlyph
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    } = string.Empty;

    public string ThemeBrushKey
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    } = PillDisplayModel.BrushNeutral;

    public bool ShowProgressRing
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    public double ProgressValue
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    /// <summary>Popover open flag, toggled by the pill click command.</summary>
    public bool IsPopoverOpen
    {
        get;
        set { if (field == value) return; field = value; Raise(); }
    }

    public RelayCommand TogglePopoverCommand { get; }

    private void OnStateChanged(object? sender, UpdateStateSnapshot snap)
    {
        _dispatcher.TryEnqueue(() => Project(snap));
    }

    private void Project(UpdateStateSnapshot snap)
    {
        var d = PillDisplayModel.MapFromState(snap);
        IsVisible = d.IsVisible;
        Label = d.Label;
        IconGlyph = d.IconGlyph;
        ThemeBrushKey = d.ThemeBrushKey;
        ShowProgressRing = d.ShowProgressRing;
        ProgressValue = d.ProgressValue;
    }

    public void Dispose()
    {
        _service.StateChanged -= OnStateChanged;
    }
}
