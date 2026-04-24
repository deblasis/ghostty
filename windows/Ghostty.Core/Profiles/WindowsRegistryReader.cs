using System;
using System.Runtime.Versioning;
using System.Security;
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
        using var key = TryOpen(hive, keyPath);
        return key?.GetValue(valueName) as string;
    }

    public bool KeyExists(RegistryHive hive, string keyPath)
    {
        using var key = TryOpen(hive, keyPath);
        return key is not null;
    }

    // Returns null on missing key OR on access-denied. Probes run speculatively
    // across candidate keys; a restricted HKLM subtree must not abort discovery.
    private static RegistryKey? TryOpen(RegistryHive hive, string keyPath)
    {
        try
        {
            var root = OpenRoot(hive);
            return root.OpenSubKey(keyPath);
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (SecurityException) { return null; }
    }

    private static RegistryKey OpenRoot(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => Registry.LocalMachine,
        RegistryHive.CurrentUser => Registry.CurrentUser,
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };
}
