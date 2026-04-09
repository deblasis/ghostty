using System;
using System.IO;
using System.Text.Json;

namespace Ghostty.Settings;

/// <summary>
/// Tiny persistent store for UI-layer preferences that need to
/// survive restarts but do not belong in libghostty's config layer
/// yet. One JSON file at
/// <c>%APPDATA%\Ghostty\ui-settings.json</c>, loaded once at
/// startup and written on every change.
///
/// TODO(config): fold this into the real config layer when it lands
/// on Windows. Until then this is the only piece of per-user UI
/// state (vertical-vs-horizontal tabs) that has to persist.
/// </summary>
internal sealed class UiSettings
{
    public bool VerticalTabs { get; set; }

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Ghostty");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "ui-settings.json");
        }
    }

    public static UiSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new UiSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiSettings>(json) ?? new UiSettings();
        }
        catch
        {
            // A malformed or inaccessible settings file must never
            // block startup. Fall back to defaults silently.
            return new UiSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Same policy as Load: never throw from a settings write.
        }
    }
}
