using System;
using System.Diagnostics;
using System.IO;
using Ghostty.Core.Config;
using Ghostty.Core.Sponsor.Update;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// Top-level sponsor wiring. Called from App.OnLaunched under
/// <c>#if SPONSOR_BUILD</c>. Constructs the update pipeline manually
/// (matches the existing App.xaml.cs pattern; no DI container in the
/// shell today). Returns a handle the app can dispose on shutdown.
/// </summary>
internal sealed class SponsorOverlayBootstrapper : IDisposable
{
    private readonly UpdateService _service;
    private readonly UpdateSimulator _simulator;
    private readonly UpdatePillViewModel _pillVm;
    private readonly UpdatePopoverViewModel _popoverVm;
    private readonly TaskbarOverlayProvider _taskbar;
    private readonly UpdateToastPublisher _toast;
    private readonly UpdateJumpListProvider _jumpList;
    private readonly RestartPendingExitInterceptor _exitInterceptor;
    private readonly SponsorActivationRouter _router;

    public SponsorActivationRouter Router => _router;
    public UpdateSimulator Simulator => _simulator;

    private SponsorOverlayBootstrapper(
        UpdateService service,
        UpdateSimulator simulator,
        UpdatePillViewModel pillVm,
        UpdatePopoverViewModel popoverVm,
        TaskbarOverlayProvider taskbar,
        UpdateToastPublisher toast,
        UpdateJumpListProvider jumpList,
        RestartPendingExitInterceptor exitInterceptor,
        SponsorActivationRouter router)
    {
        _service = service;
        _simulator = simulator;
        _pillVm = pillVm;
        _popoverVm = popoverVm;
        _taskbar = taskbar;
        _toast = toast;
        _jumpList = jumpList;
        _exitInterceptor = exitInterceptor;
        _router = router;
    }

    public static SponsorOverlayBootstrapper Wire(
        Window mainWindow,
        IConfigService config,
        DispatcherQueue dispatcher)
    {
        var simulator = new UpdateSimulator();
        var service = new UpdateService(simulator, config);

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var skipPath = Path.Combine(localAppData, "Ghostty", "update-skips.json");
        var skipList = new UpdateSkipList(skipPath);

        var pillVm = new UpdatePillViewModel(service, dispatcher);
        var popoverVm = new UpdatePopoverViewModel(service, skipList, dispatcher);

        // Inject the pill into the title bar overlay presenter.
        var host = mainWindow.Content is FrameworkElement root
            ? root.FindName("SponsorOverlayHost") as ContentPresenter
            : null;
        if (host is not null)
        {
            var pill = new UpdatePill();
            pill.Bind(pillVm, popoverVm);
            host.Content = pill;
        }
        else
        {
            Debug.WriteLine("[sponsor/update] SponsorOverlayHost not found; pill will not render");
        }

        var taskbar = new TaskbarOverlayProvider(service, dispatcher);
        taskbar.Attach(mainWindow);
        var toast = new UpdateToastPublisher(service, dispatcher);
        toast.Attach(mainWindow);
        var jumpList = new UpdateJumpListProvider(service);
        jumpList.Attach();
        var exit = new RestartPendingExitInterceptor(service, dispatcher);
        exit.Attach(mainWindow);

        var router = new SponsorActivationRouter(service, dispatcher);

        var exePath = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrEmpty(exePath))
        {
            WinttyProtocolRegistrar.EnsureRegistered(exePath);
        }

        service.Start();
        return new SponsorOverlayBootstrapper(
            service, simulator, pillVm, popoverVm,
            taskbar, toast, jumpList, exit, router);
    }

    public void Dispose()
    {
        _exitInterceptor.Dispose();
        _jumpList.Dispose();
        _toast.Dispose();
        _taskbar.Dispose();
        _popoverVm.Dispose();
        _pillVm.Dispose();
        _service.Dispose();
    }

#if DEBUG
    /// <summary>
    /// Debug-only: Ctrl+Shift+Alt+1..8 hotkeys that drive the simulator
    /// through the full state machine for manual QA.
    /// </summary>
    public static void AttachSimulatorShortcuts(Window window, UpdateSimulator sim)
    {
        if (window.Content is not Microsoft.UI.Xaml.UIElement root) return;

        void Bind(Windows.System.VirtualKey key, Ghostty.Core.Sponsor.Update.UpdateState state, string? version = null, double? progress = null, string? err = null)
        {
            var accel = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = key,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
                          | Windows.System.VirtualKeyModifiers.Shift
                          | Windows.System.VirtualKeyModifiers.Menu,
                ScopeOwner = root,
            };
            accel.Invoked += (s, e) =>
            {
                sim.Simulate(state, version, progress, err);
                e.Handled = true;
            };
            root.KeyboardAccelerators.Add(accel);
        }

        Bind(Windows.System.VirtualKey.Number1, Ghostty.Core.Sponsor.Update.UpdateState.Idle);
        Bind(Windows.System.VirtualKey.Number2, Ghostty.Core.Sponsor.Update.UpdateState.NoUpdatesFound);
        Bind(Windows.System.VirtualKey.Number3, Ghostty.Core.Sponsor.Update.UpdateState.UpdateAvailable, version: "1.4.2");
        Bind(Windows.System.VirtualKey.Number4, Ghostty.Core.Sponsor.Update.UpdateState.Downloading, progress: 0.42);
        Bind(Windows.System.VirtualKey.Number5, Ghostty.Core.Sponsor.Update.UpdateState.Extracting);
        Bind(Windows.System.VirtualKey.Number6, Ghostty.Core.Sponsor.Update.UpdateState.Installing);
        Bind(Windows.System.VirtualKey.Number7, Ghostty.Core.Sponsor.Update.UpdateState.RestartPending);
        Bind(Windows.System.VirtualKey.Number8, Ghostty.Core.Sponsor.Update.UpdateState.Error, err: "Simulated failure");
    }
#endif
}
