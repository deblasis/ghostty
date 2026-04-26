using Ghostty.Core.Input;
using Xunit;

namespace Ghostty.Tests.Input;

public class PaneActionTests
{
    [Theory]
    [InlineData(PaneAction.OpenProfile1, 28)]
    [InlineData(PaneAction.OpenProfile2, 29)]
    [InlineData(PaneAction.OpenProfile3, 30)]
    [InlineData(PaneAction.OpenProfile4, 31)]
    [InlineData(PaneAction.OpenProfile5, 32)]
    [InlineData(PaneAction.OpenProfile6, 33)]
    [InlineData(PaneAction.OpenProfile7, 34)]
    [InlineData(PaneAction.OpenProfile8, 35)]
    [InlineData(PaneAction.OpenProfile9, 36)]
    public void OpenProfileN_HasContiguousValuesAfterToggleCommandPalette(PaneAction action, int expected)
    {
        Assert.Equal(expected, (int)action);
    }
}
