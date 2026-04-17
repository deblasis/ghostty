using System.Runtime.Versioning;
using Ghostty.Bench.Transports;
using Xunit;

namespace Ghostty.Tests.Bench;

public class DirectPipeTransportTests
{
    // DirectPipe has no conhost-style preamble to drain, so WaitReady must
    // return immediately. This test pins the no-op contract so a future
    // change to DirectPipe cannot silently add latency to every probe.
    [SupportedOSPlatform("windows")]
    [Fact(Timeout = 10_000)]
    public async Task WaitReady_IsNoop()
    {
        string childPath = Path.Combine(AppContext.BaseDirectory, "Ghostty.Bench.EchoChild.exe");
        Assert.True(File.Exists(childPath), $"EchoChild not copied: {childPath}");

        using var transport = new DirectPipeTransport(childPath);

        // Zero-timeout: if WaitReady is truly a no-op it returns instantly;
        // if it ever starts blocking, the zero timeout exposes that. Wrapped
        // in Task.Run to satisfy xunit 2.9's requirement that [Fact(Timeout)]
        // tests be async, matching the pattern used in ConPtyTransportTests.
        await Task.Run(() => transport.WaitReady(TimeSpan.Zero));
    }
}
