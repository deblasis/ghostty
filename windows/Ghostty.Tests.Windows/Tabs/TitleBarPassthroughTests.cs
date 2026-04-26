using Xunit;

namespace Ghostty.Tests.Windows.Tabs;

public class TitleBarPassthroughTests
{
    // Skipped until WinUITestHost is wired here (shared with
    // NewTabSplitButtonSmokeTests). Tracked at # 351. The Assert.Fail
    // placeholder otherwise turns every test run red, including the
    // wintty-release bumper validation.
    [Fact(Skip = "TODO: wire WinUITestHost shared with NewTabSplitButtonSmokeTests; see # 351")]
    public void SplitButton_BoundsAreNotInsideTitleBarElement()
    {
        // Intended assertions:
        //   1. Find the SplitButton ('ButtonRoot') inside MainWindow.
        //   2. Find the element passed to AppWindow.SetTitleBar
        //      (TabHost.CustomDragRegion = the cell-1 spacer).
        //   3. Compute SplitButton.TransformToVisual(spacer)
        //      .TransformBounds(Rect.Empty).
        //   4. Assert the SplitButton bounds are NOT contained within
        //      the spacer's bounds (i.e. they live in cell 0, not cell 1).
    }
}
