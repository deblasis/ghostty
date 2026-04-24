using System.Threading;
using Ghostty.Core.Profiles;
using Ghostty.Tests.Profiles.Fakes;
using Xunit;

namespace Ghostty.Tests.Profiles;

public sealed class WindowsIconResolverBundledKeyTests
{
    [Fact]
    public async System.Threading.Tasks.Task Resolve_KnownBundledKey_ReturnsNonEmptyPng()
    {
        var fs = new FakeFileSystem();
        fs.SetKnownFolder(KnownFolderId.LocalAppData, @"C:\cache");
        var resolver = new WindowsIconResolver(fs);

        var bytes = await resolver.ResolveAsync(new IconSpec.BundledKey("cmd"), CancellationToken.None);

        Assert.NotEmpty(bytes);
        // Real PNG signature: 89 50 4E 47.
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_UnknownBundledKey_FallsBackToDefault()
    {
        var fs = new FakeFileSystem();
        fs.SetKnownFolder(KnownFolderId.LocalAppData, @"C:\cache");
        var resolver = new WindowsIconResolver(fs);

        var bytes = await resolver.ResolveAsync(new IconSpec.BundledKey("nonexistent"), CancellationToken.None);

        Assert.NotEmpty(bytes); // fell back to default.png
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_PathSpec_ReadsFileBytes()
    {
        var fs = new FakeFileSystem();
        fs.SetKnownFolder(KnownFolderId.LocalAppData, @"C:\cache");
        var iconBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        fs.AddFile(@"C:\myicon.png", iconBytes);

        var resolver = new WindowsIconResolver(fs);
        var bytes = await resolver.ResolveAsync(new IconSpec.Path(@"C:\myicon.png"), CancellationToken.None);

        Assert.Equal(iconBytes, bytes);
    }
}
