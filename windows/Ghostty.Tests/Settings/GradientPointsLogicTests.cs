using Ghostty.Core.Settings;
using Xunit;

namespace Ghostty.Tests.Settings;

public class GradientPointsLogicTests
{
    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f)]
    [InlineData(-0.1f, 0.4f, 0.0f, 0.4f)]
    [InlineData(0.4f, 1.2f, 0.4f, 1.0f)]
    [InlineData(2.0f, -3.0f, 1.0f, 0.0f)]
    public void Clamp_ClampsXAndY(float x, float y, float expectedX, float expectedY)
    {
        var (cx, cy) = GradientPointsLogic.Clamp(x, y);
        Assert.Equal(expectedX, cx);
        Assert.Equal(expectedY, cy);
    }
}
