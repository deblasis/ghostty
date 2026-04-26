using System;
using System.Diagnostics;
using Ghostty.Controls.Settings;
using Ghostty.Core.Config;
using Ghostty.Core.Profiles;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Settings.Pages;

/// <summary>
/// Read-only listing of the visible profiles plus the resolved
/// default-profile id, with a per-row toggle that flips the profile's
/// hidden state. Re-binds whenever the registry recomposes (config
/// reload or background discovery refresh). Hidden expander and
/// parser-warning surface land in subsequent commits.
/// </summary>
internal sealed partial class ProfilesPage : Page
{
    private readonly IProfileRegistry _registry;
    private readonly IConfigService _configService;
    private readonly IConfigFileEditor _editor;

    public ProfilesPage(
        IProfileRegistry registry,
        IConfigService configService,
        IConfigFileEditor editor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(editor);
        _registry = registry;
        _configService = configService;
        _editor = editor;
        InitializeComponent();

        // Subscribe at Loaded / unsubscribe at Unloaded so a cached
        // Page that gets navigated away from doesn't keep holding the
        // registry's event source alive past its useful lifetime.
        Loaded += (_, _) =>
        {
            Rebind();
            _registry.ProfilesChanged += OnProfilesChanged;
        };
        Unloaded += (_, _) => _registry.ProfilesChanged -= OnProfilesChanged;
    }

    // ProfilesChanged is raised on the UI dispatcher per
    // IProfileRegistry's contract, but TryEnqueue keeps Rebind safe if
    // a future implementation ever fires synchronously from a
    // background thread.
    private void OnProfilesChanged(IProfileRegistry _) =>
        DispatcherQueue.TryEnqueue(Rebind);

    private void Rebind()
    {
        DefaultProfileCard.Description =
            _registry.DefaultProfileId ?? "(no default profile set)";

        // Populate SettingsGroup.Cards directly rather than nesting an
        // ItemsControl: SettingsGroup wraps Cards in a Spacing-aware
        // StackPanel, so per-row separation Just Works. This also
        // sidesteps the DataTemplate x:Bind-vs-ContainerContentChanging
        // wrinkle the rest of this codebase has worked around.
        ProfilesGroup.Cards.Clear();
        foreach (var profile in _registry.Profiles)
        {
            ProfilesGroup.Cards.Add(BuildRow(profile, isHidden: false));
        }
    }

    // Builds one SettingsCard with a ToggleSwitch on the right. The
    // toggle's current state mirrors the row's section: off in the
    // visible list, on in the hidden list (when that section lands in
    // a follow-up commit). Tag carries the profile id so the handler
    // does not need a per-row closure capture.
    private SettingsCard BuildRow(ResolvedProfile profile, bool isHidden)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isHidden,
            OffContent = "Visible",
            OnContent = "Hidden",
            Tag = profile.Id,
        };
        toggle.Toggled += OnHiddenToggled;
        return new SettingsCard
        {
            Header = profile.Name,
            Description = profile.Command,
            Control = toggle,
        };
    }

    private void OnHiddenToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        if (toggle.Tag is not string id) return;
        try
        {
            // Mirror the AppearancePage / RawEditorPage pattern: suppress
            // the FileSystemWatcher around our own write so its 300ms
            // debounce doesn't double-fire Reload, then call Reload
            // explicitly so the in-memory ProfileView updates and the
            // page rebinds deterministically rather than depending on
            // when the watcher's debounce timer happens to land.
            _configService.SuppressWatcher(true);
            try { _editor.SetValue(ProfileHiddenKey.For(id), toggle.IsOn ? "true" : "false"); }
            finally { _configService.SuppressWatcher(false); }
            _configService.Reload();
        }
        catch (Exception ex)
        {
            // SetValue / Reload failure leaves the toggle visually
            // flipped; the next ProfilesChanged event from a successful
            // reload would either confirm or revert it. Logged for
            // diagnostic only -- no user-facing surface in V1.
            Debug.WriteLine($"failed to toggle profile {id} hidden: {ex}");
        }
    }
}
