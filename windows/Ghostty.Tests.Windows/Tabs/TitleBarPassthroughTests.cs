using Xunit;

namespace Ghostty.Tests.Windows.Tabs;

public class TitleBarPassthroughTests
{
    // Placeholder added in PR # 345 with an unimplemented body (needs
    // the same WinUITestHost as NewTabSplitButtonSmokeTests). Skipped
    // instead of Assert.Fail so the suite reports a known-skip rather
    // than red on every run, including the wintty-release bumper
    // validation. Tracked at # 351.
    [Fact(Skip = "TODO: wire WinUITestHost. See file header comment and # 351.")]
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
