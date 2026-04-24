using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Logging;
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

    [Fact]
    public async Task DiscoverAsync_FreshCacheWithinTtl_SkipsProbes()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero) };
        var fs = new Ghostty.Tests.Profiles.Fakes.FakeFileSystem();

        var cached = new DiscoveryCacheFile(
            SchemaVersion: DiscoveryCache.CurrentSchemaVersion,
            WinttyVersion: "1.2.3",
            CreatedAt: clock.UtcNow.AddHours(-1),
            Profiles: new List<DiscoveryCacheEntry>
            {
                new("cmd", "Command Prompt", "cmd.exe", "cmd", null, null, null),
            });
        fs.AddFile(@"C:\cache\v1.json", DiscoveryCache.Serialize(cached));

        var probe = new Ghostty.Tests.Profiles.Fakes.FakeInstalledShellProbe("cmd", null);
        var svc = new DiscoveryService(new[] { probe }, fs, clock,
            winttyVersion: "1.2.3", cacheFilePath: @"C:\cache\v1.json");

        var result = await svc.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("cmd", result[0].Id);
        // probe.DiscoverAsync was NOT called -> no way to directly assert on
        // FakeInstalledShellProbe today; add a Calls counter in the fake if
        // needed. For now assert via the cached data round-trip that the
        // probe did not run (probe returned null).
    }

    [Fact]
    public async Task DiscoverAsync_CacheOlderThan24h_ReRunsProbes()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero) };
        var fs = new Ghostty.Tests.Profiles.Fakes.FakeFileSystem();

        var stale = new DiscoveryCacheFile(
            SchemaVersion: DiscoveryCache.CurrentSchemaVersion,
            WinttyVersion: "1.2.3",
            CreatedAt: clock.UtcNow.AddHours(-25),
            Profiles: new List<DiscoveryCacheEntry>());
        fs.AddFile(@"C:\cache\v1.json", DiscoveryCache.Serialize(stale));

        var probe = new Ghostty.Tests.Profiles.Fakes.FakeInstalledShellProbe("cmd", new[]
        {
            new DiscoveredProfile("cmd", "Command Prompt", "cmd.exe", "cmd"),
        });
        var svc = new DiscoveryService(new[] { probe }, fs, clock,
            winttyVersion: "1.2.3", cacheFilePath: @"C:\cache\v1.json");

        var result = await svc.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        // Also assert cache was refreshed: file now has freshly-dated cache.
        var rewritten = DiscoveryCache.Deserialize(await fs.ReadAllBytesAsync(@"C:\cache\v1.json", CancellationToken.None));
        Assert.Equal(clock.UtcNow, rewritten!.CreatedAt);
    }

    [Fact]
    public async Task DiscoverAsync_WinttyVersionBump_InvalidatesCache()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero) };
        var fs = new Ghostty.Tests.Profiles.Fakes.FakeFileSystem();

        var cached = new DiscoveryCacheFile(
            SchemaVersion: DiscoveryCache.CurrentSchemaVersion,
            WinttyVersion: "1.2.3", // old
            CreatedAt: clock.UtcNow.AddHours(-1),
            Profiles: new List<DiscoveryCacheEntry>());
        fs.AddFile(@"C:\cache\v1.json", DiscoveryCache.Serialize(cached));

        var probe = new Ghostty.Tests.Profiles.Fakes.FakeInstalledShellProbe("cmd", new[]
        {
            new DiscoveredProfile("cmd", "Command Prompt", "cmd.exe", "cmd"),
        });
        var svc = new DiscoveryService(new[] { probe }, fs, clock,
            winttyVersion: "1.2.4", // new
            cacheFilePath: @"C:\cache\v1.json");

        var result = await svc.DiscoverAsync(CancellationToken.None);
        Assert.Single(result);
    }

    internal sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
