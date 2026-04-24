using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Profiles;
using Ghostty.Tests.Profiles.Fakes;
using Xunit;

namespace Ghostty.Tests.Profiles;

public sealed class DiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_TwoProbes_MergesResultsInStableOrder()
    {
        var cmdProbe = new FakeInstalledShellProbe("cmd", new[]
        {
            new DiscoveredProfile("cmd", "Command Prompt", "cmd.exe", "cmd"),
        });
        var wslProbe = new FakeInstalledShellProbe("wsl", new[]
        {
            new DiscoveredProfile("wsl-ubuntu", "WSL: Ubuntu", "wsl.exe -d Ubuntu", "wsl"),
        });

        var svc = new DiscoveryService(new[] { cmdProbe, wslProbe });
        var result = await svc.DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("cmd", result[0].Id);
        Assert.Equal("wsl-ubuntu", result[1].Id);
    }

    [Fact]
    public async Task DiscoverAsync_OneProbeThrows_OtherProbeResultsStillReturned()
    {
        var throwing = new FakeInstalledShellProbe("bad", null, throwOnDiscover: true);
        var good = new FakeInstalledShellProbe("good", new[]
        {
            new DiscoveredProfile("cmd", "Command Prompt", "cmd.exe", "good"),
        });

        var svc = new DiscoveryService(new[] { throwing, good });
        var result = await svc.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("cmd", result[0].Id);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyProbes_ReturnsEmpty()
    {
        var svc = new DiscoveryService(System.Array.Empty<IInstalledShellProbe>());
        var result = await svc.DiscoverAsync(CancellationToken.None);
        Assert.Empty(result);
    }
}
