using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ghostty.Core.Profiles;

/// <summary>
/// Composition service: merges <see cref="IProfileConfigSource"/>
/// user-defined profiles with a discovery probe run into the ordered
/// snapshot consumed by the settings-UI and command-palette consumers.
/// Dispatches <see cref="IProfileRegistry.ProfilesChanged"/> on the UI
/// thread via an injected <c>Action&lt;Action&gt;</c> (wraps
/// <c>DispatcherQueue.TryEnqueue</c> in the production wiring).
/// </summary>
internal sealed partial class ProfileRegistry : IProfileRegistry
{
    private readonly IProfileConfigSource _source;
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<DiscoveredProfile>>> _discover;
    private readonly Action<Action> _dispatcher;
    private readonly ILogger<ProfileRegistry> _log;
    private readonly Lock _sync = new();

    private volatile IReadOnlyList<ResolvedProfile> _profiles = Array.Empty<ResolvedProfile>();
    private volatile FrozenDictionary<string, ResolvedProfile> _byId =
        FrozenDictionary<string, ResolvedProfile>.Empty;
    private long _version;

    private IReadOnlyList<DiscoveredProfile> _discovered = Array.Empty<DiscoveredProfile>();

    public event Action<IProfileRegistry>? ProfilesChanged;

    public IReadOnlyList<ResolvedProfile> Profiles => _profiles;
    public string? DefaultProfileId { get; private set; }
    public long Version => Interlocked.Read(ref _version);

    public ProfileRegistry(
        IProfileConfigSource source,
        Func<bool, CancellationToken, Task<IReadOnlyList<DiscoveredProfile>>> discover,
        Action<Action> dispatcher,
        ILogger<ProfileRegistry>? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(discover);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _source = source;
        _discover = discover;
        _dispatcher = dispatcher;
        _log = log ?? NullLogger<ProfileRegistry>.Instance;

        RecomposeAndFire();
    }

    private void RecomposeAndFire()
    {
        IReadOnlyList<ResolvedProfile> next;
        FrozenDictionary<string, ResolvedProfile> nextById;
        string? nextDefault;

        lock (_sync)
        {
            var resolved = ProfileOrderResolver.Resolve(
                user: [.. _source.ParsedProfiles.Values],
                discovered: _discovered,
                profileOrder: _source.ProfileOrder,
                defaultProfileId: _source.DefaultProfileId,
                hidden: _source.HiddenProfileIds);

            next = resolved;
            var dict = new Dictionary<string, ResolvedProfile>(resolved.Count, StringComparer.OrdinalIgnoreCase);
            nextDefault = null;
            foreach (var p in resolved)
            {
                dict[p.Id] = p;
                if (p.IsDefault) nextDefault = p.Id;
            }
            nextById = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        _profiles = next;
        _byId = nextById;
        DefaultProfileId = nextDefault;
        var newVersion = Interlocked.Increment(ref _version);
        LogRecomposed(newVersion, next.Count);

        _dispatcher(() => ProfilesChanged?.Invoke(this));
    }

    public ResolvedProfile? Resolve(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return _byId.TryGetValue(profileId, out var p) ? p : null;
    }

    public Task RefreshDiscoveryAsync(CancellationToken ct)
        => throw new NotImplementedException();

    public void Dispose()
    {
        // Resources added in later tasks (CancellationTokenSource for
        // background discovery, config-changed subscription). Nothing
        // to release for the ctor-only compose path.
    }

    [LoggerMessage(EventId = LogEvents.Profiles.RegistryRecomposed,
                   Level = LogLevel.Debug,
                   Message = "registry recomposed: version={Version} count={Count}")]
    private partial void LogRecomposed(long version, int count);
}
