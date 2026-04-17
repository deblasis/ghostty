using System;
using Ghostty.Core.Sponsor.Update;
using Ghostty.Mvvm;
using Microsoft.UI.Dispatching;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// ViewModel for the update popover. Exposes one command per button
/// with state-aware enablement. CanExecute is re-queried on every
/// state transition so disabled buttons correctly gray out.
/// </summary>
internal sealed class UpdatePopoverViewModel : NotifyBase, IDisposable
{
    private readonly UpdateService _service;
    private readonly UpdateSkipList _skipList;
    private readonly DispatcherQueue _dispatcher;

    public UpdatePopoverViewModel(
        UpdateService service,
        UpdateSkipList skipList,
        DispatcherQueue dispatcher)
    {
        _service = service;
        _skipList = skipList;
        _dispatcher = dispatcher;

        SkipCommand = new RelayCommand(Skip, CanSkip);
        InstallAndRelaunchCommand = new AsyncRelayCommand(_service.DownloadAsync, CanInstallNow);
        RestartNowCommand = new AsyncRelayCommand(_service.ApplyAndRestartAsync, CanRestart);
        RetryCommand = new AsyncRelayCommand(_service.CheckNowAsync, CanRetry);

        var c = _service.Current;
        State = c.State;
        TargetVersion = c.TargetVersion;
        ErrorMessage = c.ErrorMessage;

        _service.StateChanged += OnStateChanged;
    }

    public UpdateState State
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    public string? TargetVersion
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    public string? ErrorMessage
    {
        get;
        private set { if (field == value) return; field = value; Raise(); }
    }

    public RelayCommand SkipCommand { get; }
    public AsyncRelayCommand InstallAndRelaunchCommand { get; }
    public AsyncRelayCommand RestartNowCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }

    private void Skip()
    {
        if (!string.IsNullOrEmpty(TargetVersion))
        {
            _skipList.Skip(TargetVersion);
        }
    }

    private bool CanSkip() =>
        State == UpdateState.UpdateAvailable && !string.IsNullOrEmpty(TargetVersion);

    private bool CanInstallNow() => State == UpdateState.UpdateAvailable;
    private bool CanRestart() => State == UpdateState.RestartPending;
    private bool CanRetry() => State == UpdateState.Error;

    private void OnStateChanged(object? sender, UpdateStateSnapshot snap)
    {
        _dispatcher.TryEnqueue(() =>
        {
            State = snap.State;
            TargetVersion = snap.TargetVersion;
            ErrorMessage = snap.ErrorMessage;
            SkipCommand.RaiseCanExecuteChanged();
            InstallAndRelaunchCommand.RaiseCanExecuteChanged();
            RestartNowCommand.RaiseCanExecuteChanged();
            RetryCommand.RaiseCanExecuteChanged();
        });
    }

    public void Dispose()
    {
        _service.StateChanged -= OnStateChanged;
    }
}
