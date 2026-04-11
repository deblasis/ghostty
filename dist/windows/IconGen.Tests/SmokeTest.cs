using Xunit;

namespace Ghostty.IconGen.Tests;

public class SmokeTest
{
    [Fact]
    public void ProgramMainReturnsZeroOnEmptyArgs()
    {
        var exitCode = Program.Main(Array.Empty<string>());
        Assert.Equal(0, exitCode);
    }
}
