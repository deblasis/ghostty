# Update UI - developer notes (D.1)

Everything here is dev-only and only active under `SPONSOR_BUILD` + `DEBUG`.

## Running

```
$env:SPONSOR_BUILD='true'
just run-win
```

## Keyboard shortcuts (drive the UpdateSimulator)

| Shortcut | State |
|---|---|
| `Ctrl+Shift+Alt+1` | Idle |
| `Ctrl+Shift+Alt+2` | NoUpdatesFound |
| `Ctrl+Shift+Alt+3` | UpdateAvailable (v1.4.2) |
| `Ctrl+Shift+Alt+4` | Downloading (42%) |
| `Ctrl+Shift+Alt+5` | Extracting |
| `Ctrl+Shift+Alt+6` | Installing |
| `Ctrl+Shift+Alt+7` | RestartPending |
| `Ctrl+Shift+Alt+8` | Error |

## Command palette (Ctrl+Shift+P)

Entries under the "Custom" category mirror the shortcuts. D.1 limitation: the
palette is built inside `MainWindow`'s constructor, which runs before the
sponsor bootstrapper wires `_sponsorOverlay`. On the first window the sponsor
source is therefore skipped. The keyboard shortcuts always work because they
attach post-construction. Re-registering the palette after `Wire` completes is
deferred to D.3.

## What each state should show

- **Pill** in the title bar: see PillDisplayModel.MapFromState for the full mapping.
- **Taskbar overlay**: appears for UpdateAvailable / RestartPending / Error only.
- **Toast**: fires once when entering UpdateAvailable or RestartPending *and* the window is unfocused *and* Focus Assist is not on.
- **Jump list**: "Install Pending Update" under "Updates" for UpdateAvailable / RestartPending.
- **Exit dialog**: appears only when closing the window in RestartPending.

## What is NOT working in D.1

- Real Velopack integration - simulator only.
- OAuth / sponsor auth - deferred to D.2.
- `auto-update` mode interplay - deferred to D.3.
- Release notes links - deferred to D.3.
- "Unobtrusive target" check (no-tab suppression) - deferred to D.3.
- Command palette sponsor entries on first-window init (see note above).

## Threading contract

Every class subscribing to `UpdateService.StateChanged` (TaskbarOverlayProvider,
UpdateToastPublisher, RestartPendingExitInterceptor) takes a `DispatcherQueue`
in its constructor and uses `TryEnqueue` to hop back to the UI thread before
touching WinUI or STA COM state. `UpdateJumpListProvider` is the exception -
its WinRT `JumpList.LoadCurrentAsync` is thread-safe. Mirror the dispatcher
pattern in any new subscriber; see `UpdatePillViewModel` for the reference.

## Windows SDK projections

`FocusAssistProbe` references `Windows.UI.Shell.FocusSessionManager` which only
ships in the 22000+ SDK projections. The base TFM is 19041 to keep OSS builds
on older toolchains; sponsor builds bump `WindowsSdkPackageVersion` to
`10.0.26100.57` inside the `SponsorBuild=true` PropertyGroup. A runtime
`IsSupported` check plus a try/catch keep the binary safe on Win10 hosts where
the type is absent.

## Native handle leak in TaskbarOverlayProvider

`LoadImage` loads each overlay icon but the returned `HICON` is never released.
Windows reclaims process-scope handles on exit. State transitions are infrequent
in practice, so the leak is bounded. D.3 may cache the three icons if profiling
shows a hot path.
