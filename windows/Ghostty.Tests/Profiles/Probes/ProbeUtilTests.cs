using Ghostty.Core.Profiles.Probes;
using Xunit;

namespace Ghostty.Tests.Profiles.Probes;

public sealed class ProbeUtilTests
{
    [Fact]
    public void QuoteIfNeeded_PathWithoutSpace_ReturnsUnchanged()
    {
        var result = ProbeUtil.QuoteIfNeeded(@"C:\Windows\System32\cmd.exe");
        Assert.Equal(@"C:\Windows\System32\cmd.exe", result);
    }

    [Fact]
    public void QuoteIfNeeded_PathWithSpace_ReturnsQuoted()
    {
        var result = ProbeUtil.QuoteIfNeeded(@"C:\Program Files\Git\bin\bash.exe");
        Assert.Equal(@"""C:\Program Files\Git\bin\bash.exe""", result);
    }

    [Fact]
    public void QuoteIfNeeded_AlreadyQuoted_ReturnsUnchanged()
    {
        var input = @"""C:\Program Files\Git\bin\bash.exe""";
        var result = ProbeUtil.QuoteIfNeeded(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void QuoteIfNeeded_EmptyString_ReturnsEmpty()
    {
        var result = ProbeUtil.QuoteIfNeeded(string.Empty);
        Assert.Equal(string.Empty, result);
    }
}
