using System;
using Ghostty.Controls.Settings;
using Ghostty.Core.Profiles;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Settings.Pages;

/// <summary>
/// Read-only listing of the visible profiles plus the resolved
/// default-profile id. Re-binds whenever the registry recomposes (config
/// reload or background discovery refresh). Hide-toggle, hidden expander,
/// and parser-warning surface land in subsequent commits.
/// </summary>
internal sealed partial class ProfilesPage : Page
{
    private readonly IProfileRegistry _registry;

    public ProfilesPage(IProfileRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
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
            ProfilesGroup.Cards.Add(new SettingsCard
            {
                Header = profile.Name,
                Description = profile.Command,
            });
        }
    }
}
