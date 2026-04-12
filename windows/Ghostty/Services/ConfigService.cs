using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Ghostty.Core.Config;
using Ghostty.Interop;
using Microsoft.UI.Dispatching;

namespace Ghostty.Services;

/// <summary>
/// A single gradient color point with normalized position, color, and radius.
/// </summary>
internal readonly record struct GradientPoint(
    float X, float Y, Windows.UI.Color Color, float Radius);

/// <summary>
/// Owns the libghostty config lifecycle: init, load from disk,
/// reload, and file-system watching (behind <c>auto-reload-config</c>).
/// Fires <see cref="ConfigChanged"/> on the UI thread after every
/// successful reload so consumers can re-read values they depend on.
/// </summary>
internal sealed class ConfigService : IConfigService
{
    private GhosttyConfig _config;
    private GhosttyApp _app;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly Lock _timerLock = new();
    private readonly DispatcherQueue _dispatcher;
    private volatile bool _suppressWatcher;

    public event Action<IConfigService>? ConfigChanged;
    public string ConfigFilePath { get; }
    public bool AutoReloadEnabled { get; private set; }
    public bool SettingsUiEnabled { get; private set; }
    public double BackgroundOpacity { get; private set; } = 1.0;
    public string BackgroundStyle { get; private set; } = "frosted";
    public Windows.UI.Color? BackgroundTintColor { get; private set; }
    public float? BackgroundTintOpacity { get; private set; }
    public float? BackgroundLuminosityOpacity { get; private set; }
    public bool BackgroundBlurFollowsOpacity { get; private set; }
    public IReadOnlyList<GradientPoint> GradientPoints { get; private set; } = [];
    public string GradientAnimation { get; private set; } = "static";
    public float GradientSpeed { get; private set; } = 1.0f;
    public string GradientBlend { get; private set; } = "overlay";
    public float GradientOpacity { get; private set; } = 0.05f;
    public string WindowTheme { get; private set; } = "auto";
    public uint BackgroundColor { get; private set; } = 0x001E1E2E;
    public int DiagnosticsCount { get; private set; }

    /// <summary>
    /// The current config handle. Passed to <see cref="GhosttyHost"/>
    /// so it can create the app with the loaded config.
    /// </summary>
    public GhosttyConfig ConfigHandle => _config;

    public ConfigService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        NativeMethods.Init(UIntPtr.Zero, IntPtr.Zero);

        _config = NativeMethods.ConfigNew();
        NativeMethods.ConfigLoadDefaultFiles(_config);
        NativeMethods.ConfigFinalize(_config);

        var pathStr = NativeMethods.ConfigOpenPath();
        var rawPath = pathStr.Ptr != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(pathStr.Ptr, (int)pathStr.Len) ?? string.Empty
            : string.Empty;
        // Normalize mixed separators from Zig (forward slash) + Windows
        // (backslash) so the path looks clean in UI and logs.
        ConfigFilePath = Path.GetFullPath(rawPath);

        CacheDiagnostics();
        ReadFlags();
    }

    /// <summary>
    /// Must be called after <c>ghostty_app_new()</c> so reloads can
    /// push the new config into the running app via
    /// <c>ghostty_app_update_config</c>.
    /// </summary>
    public void SetApp(GhosttyApp app)
    {
        _app = app;
        if (AutoReloadEnabled) StartWatcher();
    }

    public bool Reload()
    {
        // Don't reload before the app is created -- the initial config
        // is the one passed to ghostty_app_new and must stay alive.
        if (_app.Handle == IntPtr.Zero) return false;

        GhosttyConfig newConfig;
        try
        {
            newConfig = NativeMethods.ConfigNew();
            NativeMethods.ConfigLoadDefaultFiles(newConfig);
            NativeMethods.ConfigFinalize(newConfig);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] Reload failed to create new config: {ex.Message}");
            return false;
        }

        var oldConfig = _config;

        // Suppress the watcher for the duration of the update so our
        // own config swap doesn't trigger a redundant file-change reload.
        var wasSuppressed = _suppressWatcher;
        _suppressWatcher = true;

        NativeMethods.AppUpdateConfig(_app, newConfig);

        _config = newConfig;
        CacheDiagnostics();
        ReadFlags();

        if (oldConfig.Handle != IntPtr.Zero)
            NativeMethods.ConfigFree(oldConfig);

        _suppressWatcher = wasSuppressed;

        _dispatcher.TryEnqueue(() => ConfigChanged?.Invoke(this));
        return true;
    }

    public string GetDiagnostic(int index)
    {
        if (index < 0 || index >= DiagnosticsCount) return string.Empty;
        var diag = NativeMethods.ConfigGetDiagnostic(_config, (uint)index);
        return diag.Message != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(diag.Message) ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Temporarily suppress or resume file-system watcher events.
    /// Used by <see cref="ConfigFileEditor"/> during writes so our
    /// own save does not trigger a redundant reload.
    /// </summary>
    public void SuppressWatcher(bool suppress) => _suppressWatcher = suppress;

    private void CacheDiagnostics()
    {
        DiagnosticsCount = (int)NativeMethods.ConfigDiagnosticsCount(_config);
    }

    private void ReadFlags()
    {
        AutoReloadEnabled = GetBool("auto-reload-config");
        SettingsUiEnabled = GetBool("windows-settings-ui");
        // Clamp here so all consumers get a safe [0,1] value without
        // needing their own validation. WindowTransparencyState also
        // clamps defensively as a standalone value type.
        BackgroundOpacity = Math.Clamp(GetDouble("background-opacity", 1.0), 0.0, 1.0);
        // background-style is a Windows-only key not in the Zig config
        // schema, so we read it directly from the config file.
        BackgroundStyle = GetFileValue("background-style", "frosted");
        BackgroundTintColor = ParseHexColor(GetFileValue("background-tint-color", ""));
        BackgroundTintOpacity = ParseFloat(GetFileValue("background-tint-opacity", ""));
        BackgroundLuminosityOpacity = ParseFloat(GetFileValue("background-luminosity-opacity", ""));
        BackgroundBlurFollowsOpacity = string.Equals(
            GetFileValue("background-blur-follows-opacity", "false"),
            "true", StringComparison.OrdinalIgnoreCase);
        var rawPoints = GetAllFileValues("background-gradient-point");
        var points = new List<GradientPoint>();
        foreach (var raw in rawPoints)
        {
            if (points.Count >= 5) break;
            var pt = ParseGradientPoint(raw);
            if (pt is not null) points.Add(pt.Value);
        }
        GradientPoints = points;
        GradientAnimation = GetFileValue("background-gradient-animation", "static");
        GradientSpeed = ParseFloat(GetFileValue("background-gradient-speed", "")) ?? 1.0f;
        GradientBlend = GetFileValue("background-gradient-blend", "overlay");
        GradientOpacity = ParseFloat(GetFileValue("background-gradient-opacity", "")) ?? 0.05f;
        WindowTheme = GetString("window-theme", "auto");
        BackgroundColor = GetColor("background", 0x001E1E2E);
    }

    private unsafe bool GetBool(string key)
    {
        byte result = 0;
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        fixed (byte* keyPtr = keyBytes)
        {
            var found = NativeMethods.ConfigGet(
                _config,
                (IntPtr)(&result),
                (IntPtr)keyPtr,
                (UIntPtr)keyBytes.Length);
            return found && result != 0;
        }
    }

    private unsafe double GetDouble(string key, double defaultValue)
    {
        double result = 0;
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        fixed (byte* keyPtr = keyBytes)
        {
            var found = NativeMethods.ConfigGet(
                _config,
                (IntPtr)(&result),
                (IntPtr)keyPtr,
                (UIntPtr)keyBytes.Length);
            return found ? result : defaultValue;
        }
    }

    /// <summary>
    /// Read a string-typed config value (enums are returned as
    /// NUL-terminated UTF-8 strings by <c>ghostty_config_get</c>).
    /// </summary>
    private unsafe string GetString(string key, string defaultValue)
    {
        IntPtr result = IntPtr.Zero;
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        fixed (byte* keyPtr = keyBytes)
        {
            var found = NativeMethods.ConfigGet(
                _config,
                (IntPtr)(&result),
                (IntPtr)keyPtr,
                (UIntPtr)keyBytes.Length);
            if (!found || result == IntPtr.Zero) return defaultValue;
            return Marshal.PtrToStringUTF8(result) ?? defaultValue;
        }
    }

    /// <summary>
    /// Read a Windows-only config key directly from the config file.
    /// Keys not in the Zig config schema cannot be read via
    /// <c>ghostty_config_get</c>, so we parse the file ourselves.
    /// </summary>
    private string GetFileValue(string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(ConfigFilePath) || !File.Exists(ConfigFilePath))
            return defaultValue;

        foreach (var line in File.ReadLines(ConfigFilePath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0) continue;
            var k = trimmed[..eqIndex].Trim();
            if (k != key) continue;
            var v = trimmed[(eqIndex + 1)..].Trim();
            return v.Length > 0 ? v : defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Read all values for a repeatable Windows-only config key.
    /// Returns every matching line's value in order. Keys not in
    /// the Zig config schema are parsed from the file directly.
    /// </summary>
    private List<string> GetAllFileValues(string key)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(ConfigFilePath) || !File.Exists(ConfigFilePath))
            return results;

        foreach (var line in File.ReadLines(ConfigFilePath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0) continue;
            var k = trimmed[..eqIndex].Trim();
            if (k != key) continue;
            var v = trimmed[(eqIndex + 1)..].Trim();
            if (v.Length > 0) results.Add(v);
        }
        return results;
    }

    /// <summary>
    /// Read a color config value. libghostty returns colors as
    /// <c>ghostty_config_color_s { r: u8, g: u8, b: u8 }</c>.
    /// We pack it into 0x00RRGGBB for easy consumption.
    /// </summary>
    private unsafe uint GetColor(string key, uint defaultValue)
    {
        // ghostty_config_color_s is 3 bytes: r, g, b (no padding).
        byte r = 0, g = 0, b = 0;
        // Stack a 3-byte buffer for the color struct.
        byte* colorBuf = stackalloc byte[3];
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        fixed (byte* keyPtr = keyBytes)
        {
            var found = NativeMethods.ConfigGet(
                _config,
                (IntPtr)colorBuf,
                (IntPtr)keyPtr,
                (UIntPtr)keyBytes.Length);
            if (!found) return defaultValue;
            r = colorBuf[0];
            g = colorBuf[1];
            b = colorBuf[2];
        }
        return ((uint)r << 16) | ((uint)g << 8) | b;
    }

    /// <summary>
    /// Parse a hex color string (#RGB, #RRGGBB, or #AARRGGBB) into
    /// a <see cref="Windows.UI.Color"/>. Returns null if the string
    /// is empty or not a valid hex color.
    /// </summary>
    private static Windows.UI.Color? ParseHexColor(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var hex = value.TrimStart('#');
        try
        {
            return hex.Length switch
            {
                // #RGB -> expand to #RRGGBB
                3 => Windows.UI.Color.FromArgb(0xFF,
                    byte.Parse(new string(hex[0], 2), System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(new string(hex[1], 2), System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(new string(hex[2], 2), System.Globalization.NumberStyles.HexNumber)),
                // #RRGGBB
                6 => Windows.UI.Color.FromArgb(0xFF,
                    byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber)),
                // #AARRGGBB
                8 => Windows.UI.Color.FromArgb(
                    byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber)),
                _ => null,
            };
        }
        catch (FormatException) { return null; }
    }

    private static float? ParseFloat(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, 0f, 1f)
            : null;
    }

    /// <summary>
    /// Parse a gradient point string: "x,y,#color,radius".
    /// Returns null if the format is invalid.
    /// </summary>
    private static GradientPoint? ParseGradientPoint(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return null;

        if (!float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x))
            return null;
        if (!float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var y))
            return null;
        var color = ParseHexColor(parts[2]);
        if (color is null) return null;
        if (!float.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var radius))
            return null;

        return new GradientPoint(
            Math.Clamp(x, 0f, 1f),
            Math.Clamp(y, 0f, 1f),
            color.Value,
            Math.Clamp(radius, 0f, 1f));
    }

    private void StartWatcher()
    {
        if (_watcher != null) return;
        var dir = Path.GetDirectoryName(ConfigFilePath);
        var file = Path.GetFileName(ConfigFilePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += (s, e) => OnFileChanged(s, e);
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        if (_suppressWatcher) return;
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                _dispatcher.TryEnqueue(() => Reload());
            }, null, 300, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        StopWatcher();
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
        }
        if (_config.Handle != IntPtr.Zero)
            NativeMethods.ConfigFree(_config);
    }
}
