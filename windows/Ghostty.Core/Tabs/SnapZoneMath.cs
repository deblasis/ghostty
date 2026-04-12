using System;

namespace Ghostty.Core.Tabs;

/// <summary>
/// Pure rect math for Snap Layouts zones. Integer arithmetic only:
/// work-area coordinates come from DisplayArea.WorkArea as physical
/// pixels and go straight to AppWindow.MoveAndResize, so converting
/// to double and back risks rounding off-by-ones.
///
/// The odd-width split always rounds the left half DOWN and gives
/// the remainder to the right half, keeping left + right equal to
/// the input width with no seam crossing the mid-line.
/// </summary>
internal static class SnapZoneMath
{
    public static SnapZoneRect RectFor(SnapZone zone, int x, int y, int w, int h)
    {
        int halfW = w / 2;
        int halfH = h / 2;
        int restW = w - halfW; // remainder lives in the right/bottom
        int restH = h - halfH;

        return zone switch
        {
            SnapZone.Maximize => new SnapZoneRect(x, y, w, h),

            SnapZone.LeftHalf   => new SnapZoneRect(x,         y,         halfW, h),
            SnapZone.RightHalf  => new SnapZoneRect(x + halfW, y,         restW, h),
            SnapZone.TopHalf    => new SnapZoneRect(x,         y,         w,     halfH),
            SnapZone.BottomHalf => new SnapZoneRect(x,         y + halfH, w,     restH),

            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unhandled zone"),
        };
    }
}
