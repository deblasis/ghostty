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

namespace Ghostty.Core.DirectWrite;

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
}
