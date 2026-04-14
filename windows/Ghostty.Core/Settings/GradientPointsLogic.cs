using System;
using System.Collections.Generic;

namespace Ghostty.Core.Settings;

/// <summary>
/// Pure positional helpers for <c>GradientPointsEditor</c>. Lives in
/// Ghostty.Core so it can be unit-tested without a XAML test host.
/// All coordinates are in normalized [0,1] canvas space.
/// </summary>
public static class GradientPointsLogic
{
    public static (float X, float Y) Clamp(float x, float y) =>
        (Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f));
}
