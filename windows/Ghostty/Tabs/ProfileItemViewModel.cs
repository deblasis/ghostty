using Microsoft.UI.Xaml.Media.Imaging;

namespace Ghostty.Tabs;

/// <summary>
/// Per-row data for the new-tab split button's profile dropdown.
/// Built from a <c>ResolvedProfile</c> plus an asynchronously-resolved
/// icon. The whole list is rebuilt on every
/// <c>IProfileRegistry.ProfilesChanged</c>, so this type does NOT
/// implement INPC — replacement is whole-list, not in-place.
/// </summary>
internal sealed class ProfileItemViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsDefault { get; init; }

    /// <summary>
    /// Lazily-resolved icon. Null until <c>IIconResolver.ResolveAsync</c>
    /// completes; the row falls back to the bundled placeholder until
    /// then. Set on the UI thread before assigning to the menu item's
    /// <c>Icon</c> property.
    /// </summary>
    public BitmapImage? Icon { get; set; }
}
