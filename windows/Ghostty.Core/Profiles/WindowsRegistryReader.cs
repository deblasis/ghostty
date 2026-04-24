using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Ghostty.Core.Profiles;

/// <summary>
/// Production IRegistryReader. Read-only by design; probes never write.
/// Missing keys/values return null/false rather than throwing.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string keyPath, string valueName)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public bool KeyExists(RegistryHive hive, string keyPath)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(keyPath);
        return key is not null;
    }

    private static RegistryKey OpenRoot(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => Registry.LocalMachine,
        RegistryHive.CurrentUser => Registry.CurrentUser,
        _ => throw new System.ArgumentOutOfRangeException(nameof(hive)),
    };
}
