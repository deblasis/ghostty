// Pure-logic DWrite font family enumeration shared between the
// migrated AppearancePage code path (Ghostty WinUI 3 assembly)
// and the equivalence test (Ghostty.Tests).
//
// EnumerateReference is the raw-vtable path, copied verbatim from
// the pre-migration AppearancePage.EnumerateSystemFonts and kept
// ONLY as the equivalence baseline. EnumerateMigrated (added in
// Task 7 Step 3) is the CsWin32-generated path that production
// AppearancePage will call.
//
// Do NOT delete EnumerateReference when the migration lands - it
// guards against locale-selection or vtable-slot drift in future
// revisions and is the anchor of DWriteFontFamilyEquivalenceTest.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Ghostty.Core.DirectWrite;

// Ghostty.Core targets plain net9.0 (no Windows-specific
// TargetPlatformVersion) so the CA1416 platform analyzer flags
// every CsWin32-generated DWrite call as "only supported on
// windows6.1+". The Ghostty WinUI 3 shell is windows10.0.19041,
// so all callers are gated. Mark the helper accordingly to silence
// the analyzer at the helper boundary.
[SupportedOSPlatform("windows6.1")]
public static class DWriteFontEnumerator
{
    /// <summary>
    /// Raw-vtable reference implementation. Kept verbatim from the
    /// pre-migration AppearancePage code path as the equivalence
    /// baseline for DWriteFontFamilyEquivalenceTest. Do not refactor
    /// to use generated CsWin32 types.
    /// </summary>
    public static unsafe List<string> EnumerateReference()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var iid = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
        IntPtr factory;
        if (DWriteCreateFactory(0, &iid, &factory) != 0 || factory == IntPtr.Zero)
            return new List<string>();

        try
        {
            var vtable = (IntPtr*)*(IntPtr*)factory;
            IntPtr collection;
            var getCollection = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int, int>)vtable[3];
            if (getCollection(factory, &collection, 1) != 0 || collection == IntPtr.Zero)
                return new List<string>();

            try
            {
                var cvt = (IntPtr*)*(IntPtr*)collection;
                var getCount = (delegate* unmanaged[Stdcall]<IntPtr, uint>)cvt[3];
                var getFamily = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)cvt[4];
                var count = getCount(collection);

                for (uint i = 0; i < count; i++)
                {
                    IntPtr familyPtr;
                    if (getFamily(collection, i, &familyPtr) != 0 || familyPtr == IntPtr.Zero)
                        continue;
                    try
                    {
                        var name = GetFamilyNameReference(familyPtr);
                        if (name != null) families.Add(name);
                    }
                    finally
                    {
                        Marshal.Release(familyPtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(collection);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        return families.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static unsafe string? GetFamilyNameReference(IntPtr familyPtr)
    {
        // Locale index 0 matches the pre-migration behavior.
        var fvt = (IntPtr*)*(IntPtr*)familyPtr;
        IntPtr namesPtr;
        var getNames = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)fvt[6];
        if (getNames(familyPtr, &namesPtr) != 0 || namesPtr == IntPtr.Zero)
            return null;

        try
        {
            var nvt = (IntPtr*)*(IntPtr*)namesPtr;
            var getCount = (delegate* unmanaged[Stdcall]<IntPtr, uint>)nvt[3];
            if (getCount(namesPtr) == 0) return null;

            uint len;
            var getLen = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, int>)nvt[7];
            if (getLen(namesPtr, 0, &len) != 0) return null;

            var buf = stackalloc char[(int)len + 1];
            var getString = (delegate* unmanaged[Stdcall]<IntPtr, uint, char*, uint, int>)nvt[8];
            if (getString(namesPtr, 0, buf, len + 1) != 0) return null;

            return new string(buf, 0, (int)len);
        }
        finally
        {
            Marshal.Release(namesPtr);
        }
    }

    [DllImport("dwrite.dll", EntryPoint = "DWriteCreateFactory")]
    private static extern unsafe int DWriteCreateFactory(int factoryType, Guid* iid, IntPtr* factory);

    /// <summary>
    /// CsWin32-generated IDWrite* path. Production AppearancePage
    /// delegates here. Exists in Ghostty.Core so the equivalence test
    /// can call it alongside EnumerateReference without pulling in
    /// the WinUI 3 Ghostty assembly.
    /// </summary>
    public static unsafe List<string> EnumerateMigrated()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Each HRESULT-returning call throws on failure (CsWin32
        // does NOT mark these methods [PreserveSig]) which preserves
        // the pre-migration semantics: the raw-vtable path bailed
        // out on non-zero hr, so the migrated path must not silently
        // drop errors. The catch at the bottom converts the exception
        // back into the empty-list fallback the caller expects.
        try
        {
            var iid = typeof(IDWriteFactory).GUID;
            PInvoke.DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                in iid,
                out var factoryObj).ThrowOnFailure();
            var factory = (IDWriteFactory)factoryObj;

            factory.GetSystemFontCollection(out var collection, checkForUpdates: true);

            var count = collection.GetFontFamilyCount();
            for (uint i = 0; i < count; i++)
            {
                collection.GetFontFamily(i, out var family);
                var name = GetFamilyNameMigrated(family);
                if (name != null) families.Add(name);
            }
        }
        catch (Exception)
        {
            // Enumeration failure is non-fatal (headless/test host);
            // fall through to the sorted result so the caller still
            // gets whatever we collected before the failure.
        }

        return families.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static unsafe string? GetFamilyNameMigrated(IDWriteFontFamily family)
    {
        family.GetFamilyNames(out var names);
        if (names.GetCount() == 0) return null;

        // Preserve pre-migration behavior: always read locale index 0,
        // not the UI language. Matches EnumerateReference and the
        // equivalence test baseline.
        const uint localeIndex = 0;
        names.GetStringLength(localeIndex, out uint len);

        // GetString writes (len + 1) chars including the terminator.
        Span<char> buf = stackalloc char[(int)len + 1];
        fixed (char* p = buf)
        {
            names.GetString(localeIndex, new PWSTR(p), len + 1);
        }
        return new string(buf[..(int)len]);
    }
}
