using System;
using System.Runtime.InteropServices;

namespace Ghostty.Interop;

/// <summary>
/// Win32 P/Invoke declarations for window transparency control.
/// </summary>
internal static partial class Win32Interop
{
    [LibraryImport("gdi32.dll")]
    public static partial IntPtr GetStockObject(int fnObject);

    // NULL_BRUSH (HOLLOW_BRUSH) -- transparent class brush so
    // composition alpha shows through to the desktop.
    public const int NULL_BRUSH = 5;
}
