using System.Runtime.InteropServices;
using Ghostty.Core.Interop;
using Xunit;

namespace Ghostty.Tests.Interop;

// Pins the ordinal values and struct layouts of the ghostty_action_*
// subset the Windows apprt dispatches on. If any of these drift away
// from include/ghostty.h, CI fails here instead of users seeing a
// silently-misrouted action at runtime.
//
// When libghostty rebases and these break, re-verify against
// include/ghostty.h:
//   grep -n GHOSTTY_ACTION_ include/ghostty.h | \
//     grep -nE 'SCROLLBAR|SET_TITLE|CLOSE_WINDOW|RING_BELL|PROGRESS_REPORT'
// then update the constants in Ghostty.Core/Interop/GhosttyActions.cs.
public class GhosttyActionsLayoutTests
{
    [Theory]
    [InlineData(GhosttyActionTag.Scrollbar, 26)]
    [InlineData(GhosttyActionTag.SetTitle, 32)]
    [InlineData(GhosttyActionTag.CloseWindow, 49)]
    [InlineData(GhosttyActionTag.RingBell, 50)]
    [InlineData(GhosttyActionTag.ProgressReport, 56)]
    public void ActionTag_Ordinal_Matches_Upstream(GhosttyActionTag tag, int expected)
    {
        Assert.Equal(expected, (int)tag);
    }

    [Theory]
    [InlineData(GhosttyProgressState.Remove, 0)]
    [InlineData(GhosttyProgressState.Set, 1)]
    [InlineData(GhosttyProgressState.Error, 2)]
    [InlineData(GhosttyProgressState.Indeterminate, 3)]
    [InlineData(GhosttyProgressState.Pause, 4)]
    public void ProgressState_Ordinal_Matches_Upstream(GhosttyProgressState state, int expected)
    {
        Assert.Equal(expected, (int)state);
    }

    [Fact]
    public void ScrollbarStruct_Size_Is_24_Bytes()
    {
        // { uint64 total; uint64 offset; uint64 len; } -> 3 * 8 = 24
        Assert.Equal(24, Marshal.SizeOf<GhosttyActionScrollbar>());
    }

    [Fact]
    public void ScrollbarStruct_Field_Offsets_Match_C_Layout()
    {
        // GhosttyHost reads this struct at (actionPtr + 8) via
        // Unsafe.ReadUnaligned, so the three fields MUST sit at
        // +0/+8/+16 within the struct itself.
        Assert.Equal(0,  (int)Marshal.OffsetOf<GhosttyActionScrollbar>(nameof(GhosttyActionScrollbar.Total)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<GhosttyActionScrollbar>(nameof(GhosttyActionScrollbar.Offset)));
        Assert.Equal(16, (int)Marshal.OffsetOf<GhosttyActionScrollbar>(nameof(GhosttyActionScrollbar.Len)));
    }

    [Fact]
    public void ProgressReportStruct_Field_Offsets_Match_C_Layout()
    {
        // ghostty_action_progress_report_s is read at +8/+12 inside
        // the action union. The struct itself sits at +0/+4 with the
        // sbyte right after the int32 (no packing tricks on x64).
        Assert.Equal(0, (int)Marshal.OffsetOf<GhosttyActionProgressReport>(nameof(GhosttyActionProgressReport.State)));
        Assert.Equal(4, (int)Marshal.OffsetOf<GhosttyActionProgressReport>(nameof(GhosttyActionProgressReport.Progress)));
    }
}
