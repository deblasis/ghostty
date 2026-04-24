using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ghostty.Core.Profiles;

/// <summary>
/// Composes registered IInstalledShellProbe implementations, runs them
/// in parallel, and merges results in probe-registration order. A probe
/// that throws is logged and skipped; other probes' results are still
/// returned. Caching is layered on top in Task 12.
/// </summary>
internal sealed partial class DiscoveryService
{
    private readonly IReadOnlyList<IInstalledShellProbe> _probes;
    private readonly ILogger<DiscoveryService> _log;

    public DiscoveryService(
        IEnumerable<IInstalledShellProbe> probes,
        ILogger<DiscoveryService>? log = null)
    {
        _probes = probes.ToList();
        _log = log ?? NullLogger<DiscoveryService>.Instance;
    }

    public async Task<IReadOnlyList<DiscoveredProfile>> DiscoverAsync(CancellationToken ct)
    {
        var tasks = _probes
            .Select(async probe =>
            {
                try
                {
                    return (probe.ProbeId, await probe.DiscoverAsync(ct).ConfigureAwait(false));
                }
                // Cancellation is not a probe "failure"; surface it to the caller
                // instead of being swallowed by the generic handler below.
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogProbeFailed(ex, probe.ProbeId);
                    return (probe.ProbeId, (IReadOnlyList<DiscoveredProfile>)Array.Empty<DiscoveredProfile>());
                }
            })
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var merged = new List<DiscoveredProfile>();
        foreach (var (_, items) in results)
            merged.AddRange(items);
        return merged;
    }

    [LoggerMessage(EventId = Ghostty.Core.Logging.LogEvents.Profiles.ProbeFailed,
                   Level = LogLevel.Warning,
                   Message = "probe '{ProbeId}' failed")]
    private partial void LogProbeFailed(System.Exception ex, string probeId);
}
