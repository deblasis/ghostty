using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Sponsor.Auth;
using Ghostty.Core.Sponsor.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ghostty.Tests.Sponsor.Update;

public partial class VelopackUpdateDriverTests
{
    internal sealed class FakeVelopackManager : IVelopackManager
    {
        public bool IsInstalled { get; set; } = true;
        public VelopackUpdateInfo? NextCheckResult { get; set; }
        public System.Collections.Generic.List<int> ProgressEmits { get; } = new();
        public bool ApplyCalled { get; private set; }
        public System.Exception? CheckThrows { get; set; }
        public System.Exception? DownloadThrows { get; set; }
        public System.TimeSpan DownloadDelay { get; set; } = System.TimeSpan.Zero;

        public Task<VelopackUpdateInfo?> CheckForUpdatesAsync(CancellationToken ct)
        {
            if (CheckThrows is not null) throw CheckThrows;
            return Task.FromResult(NextCheckResult);
        }

        public async Task DownloadUpdatesAsync(
            VelopackUpdateInfo info, System.IProgress<int> progress, CancellationToken ct)
        {
            if (DownloadThrows is not null) throw DownloadThrows;
            for (int p = 0; p <= 100; p += 25)
            {
                ct.ThrowIfCancellationRequested();
                if (DownloadDelay > System.TimeSpan.Zero)
                    await Task.Delay(DownloadDelay, ct);
                progress.Report(p);
                ProgressEmits.Add(p);
            }
        }

        public void ApplyUpdatesAndRestart(VelopackUpdateInfo info)
        {
            ApplyCalled = true;
        }
    }

    internal sealed class StubTokens : ISponsorTokenProvider
    {
        public string? Token { get; set; } = "valid";
        public int InvalidateCount { get; private set; }
        public Task<string?> GetTokenAsync(CancellationToken ct = default) => Task.FromResult(Token);
        public void Invalidate() { InvalidateCount++; Token = null; }
        public event System.EventHandler? TokenInvalidated;
        public void RaiseInvalidated() => TokenInvalidated?.Invoke(this, System.EventArgs.Empty);
    }

    [Fact]
    public void Current_DefaultsToIdle()
    {
        var driver = new VelopackUpdateDriver(new FakeVelopackManager(), new StubTokens(),
            NullLogger<VelopackUpdateDriver>.Instance);
        Assert.Equal(UpdateState.Idle, driver.Current.State);
    }
}
