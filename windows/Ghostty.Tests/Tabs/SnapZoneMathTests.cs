using Ghostty.Core.Tabs;
using Xunit;

namespace Ghostty.Tests.Tabs;

public class SnapZoneMathTests
{
    [Fact]
    public void Maximize_returns_input_rect()
    {
        var r = SnapZoneMath.RectFor(SnapZone.Maximize, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(0, 0, 1920, 1080), r);
    }

    [Fact]
    public void LeftHalf_splits_even_width_in_half()
    {
        var r = SnapZoneMath.RectFor(SnapZone.LeftHalf, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(0, 0, 960, 1080), r);
    }

    [Fact]
    public void RightHalf_has_matching_offset_and_remainder_width()
    {
        var r = SnapZoneMath.RectFor(SnapZone.RightHalf, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(960, 0, 960, 1080), r);
    }

    [Fact]
    public void TopHalf_and_BottomHalf_split_height()
    {
        var top = SnapZoneMath.RectFor(SnapZone.TopHalf, 0, 0, 1920, 1080);
        var bot = SnapZoneMath.RectFor(SnapZone.BottomHalf, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(0, 0, 1920, 540), top);
        Assert.Equal(new SnapZoneRect(0, 540, 1920, 540), bot);
    }

    [Fact]
    public void RespectsNonZeroOrigin_including_negative_left()
    {
        // Secondary monitor left of primary, taskbar at top.
        var r = SnapZoneMath.RectFor(SnapZone.LeftHalf, -1920, 100, 1920, 1040);
        Assert.Equal(new SnapZoneRect(-1920, 100, 960, 1040), r);
    }

    [Fact]
    public void OddWidth_rounds_LeftHalf_down_RightHalf_takes_remainder()
    {
        // Width 1921 -> left 960, right 961, right origin at 960. Sum 1921.
        var left = SnapZoneMath.RectFor(SnapZone.LeftHalf, 0, 0, 1921, 1080);
        var right = SnapZoneMath.RectFor(SnapZone.RightHalf, 0, 0, 1921, 1080);
        Assert.Equal(new SnapZoneRect(0, 0, 960, 1080), left);
        Assert.Equal(new SnapZoneRect(960, 0, 961, 1080), right);
        Assert.Equal(1921, left.Width + right.Width);
    }

    [Fact]
    public void TopLeftQuarter_1920x1080()
    {
        var r = SnapZoneMath.RectFor(SnapZone.TopLeftQuarter, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(0, 0, 960, 540), r);
    }

    [Fact]
    public void TopRightQuarter_1920x1080()
    {
        var r = SnapZoneMath.RectFor(SnapZone.TopRightQuarter, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(960, 0, 960, 540), r);
    }

    [Fact]
    public void BottomLeftQuarter_1920x1080()
    {
        var r = SnapZoneMath.RectFor(SnapZone.BottomLeftQuarter, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(0, 540, 960, 540), r);
    }

    [Fact]
    public void BottomRightQuarter_1920x1080()
    {
        var r = SnapZoneMath.RectFor(SnapZone.BottomRightQuarter, 0, 0, 1920, 1080);
        Assert.Equal(new SnapZoneRect(960, 540, 960, 540), r);
    }

    [Fact]
    public void Quarters_odd_dimensions_cover_input_without_seam()
    {
        var tl = SnapZoneMath.RectFor(SnapZone.TopLeftQuarter, 0, 0, 1921, 1081);
        var tr = SnapZoneMath.RectFor(SnapZone.TopRightQuarter, 0, 0, 1921, 1081);
        var bl = SnapZoneMath.RectFor(SnapZone.BottomLeftQuarter, 0, 0, 1921, 1081);
        var br = SnapZoneMath.RectFor(SnapZone.BottomRightQuarter, 0, 0, 1921, 1081);

        // Widths sum to 1921 on top row.
        Assert.Equal(1921, tl.Width + tr.Width);
        // Heights sum to 1081 on left column.
        Assert.Equal(1081, tl.Height + bl.Height);
        // BR starts exactly where TL ends.
        Assert.Equal(tl.X + tl.Width, br.X);
        Assert.Equal(tl.Y + tl.Height, br.Y);
    }
}
