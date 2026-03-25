using System.Runtime.InteropServices;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace Ghostty.Tests;

/// <summary>
/// Tests that validate P/Invoke argument passing and struct marshalling
/// against the native ghostty.dll. These call real native functions --
/// the DLL must be present in the output directory.
///
/// ghostty_init and anything that depends on it (config, app) are excluded
/// because of a known Zig bug with global mutable state in Windows DLLs.
/// </summary>
[TestClass]
public partial class InteropTests
{
    // --- Minimal P/Invoke declarations for test isolation ---
    // These duplicate a subset of the app's NativeMethods so the test
    // project does not need a reference to the WinUI 3 app project.

    private const string LibName = "ghostty";

    [StructLayout(LayoutKind.Sequential)]
    private struct GhosttyInfo
    {
        public int BuildMode;
        public nint Version;
        public nuint VersionLen;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_info")]
    private static partial GhosttyInfo GhosttyInfoNative();

    [LibraryImport(LibName, EntryPoint = "ghostty_translate")]
    private static partial nint GhosttyTranslate(nint key);

    // --- Tests ---

    [TestMethod]
    public void GhosttyInfo_ReturnsValidBuildMode()
    {
        var info = GhosttyInfoNative();

        // Build mode should be one of the known values (0-3)
        Assert.IsTrue(info.BuildMode >= 0 && info.BuildMode <= 3,
            $"Unexpected build mode: {info.BuildMode}");
    }

    [TestMethod]
    public void GhosttyInfo_ReturnsNonEmptyVersion()
    {
        var info = GhosttyInfoNative();

        Assert.IsGreaterThan((nuint)0, info.VersionLen, "Version length should be > 0");
        Assert.AreNotEqual(nint.Zero, info.Version, "Version pointer should not be null");
    }

    [TestMethod]
    public void GhosttyInfo_VersionStringIsValid()
    {
        var info = GhosttyInfoNative();

        var version = Marshal.PtrToStringUTF8(info.Version, (int)info.VersionLen);
        Assert.IsNotNull(version);
        Assert.IsGreaterThan(0, version.Length, "Version string should not be empty");

        // Version should start with a digit (semver)
        Assert.IsTrue(char.IsDigit(version[0]),
            $"Version should start with a digit, got: {version}");
    }

    [TestMethod]
    public void GhosttyInfo_StructSizeMatchesExpected()
    {
        // ghostty_info_s: int (4) + padding (4) + pointer (8) + size_t (8) = 24 bytes
        var size = Marshal.SizeOf<GhosttyInfo>();
        Assert.AreEqual(24, size,
            $"GhosttyInfo struct size mismatch: expected 24, got {size}");
    }

    [TestMethod]
    public void GhosttyInfo_CalledMultipleTimes_ReturnsSameResult()
    {
        var info1 = GhosttyInfoNative();
        var info2 = GhosttyInfoNative();

        Assert.AreEqual(info1.BuildMode, info2.BuildMode);
        Assert.AreEqual(info1.Version, info2.Version);
        Assert.AreEqual(info1.VersionLen, info2.VersionLen);
    }

    [TestMethod]
    public void GhosttyTranslate_WithNullKey_DoesNotCrash()
    {
        // ghostty_translate accepts a const char* and returns a const char*.
        // Passing null should not crash (returns null or the input).
        var result = GhosttyTranslate(nint.Zero);
        // We just care that it didn't crash. Result may be null.
    }

    [TestMethod]
    public unsafe void GhosttyTranslate_WithValidKey_DoesNotCrash()
    {
        var key = System.Text.Encoding.UTF8.GetBytes("test\0");
        fixed (byte* keyPtr = key)
        {
            var result = GhosttyTranslate((nint)keyPtr);
            // Just verifying it doesn't crash with a valid string argument.
        }
    }
}
