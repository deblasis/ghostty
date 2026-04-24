using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ghostty.Tests.Profiles;

public class ProfileRegistryTests
{
    // Test shim: the registry takes a dispatch delegate (Action<Action>)
    // so tests can run everything synchronously on the calling thread.
    private static readonly Action<Action> SynchronousDispatcher = a => a();

    // Discovery delegate returning an empty list synchronously. Later
    // tests use a TaskCompletionSource to control completion timing.
    private static Func<bool, CancellationToken, Task<IReadOnlyList<DiscoveredProfile>>> EmptyDiscovery()
        => (_, _) => Task.FromResult<IReadOnlyList<DiscoveredProfile>>(Array.Empty<DiscoveredProfile>());

    private static ProfileDef UserDef(string id, string name = "", string command = "cmd.exe")
        => new(
            Id: id,
            Name: name.Length > 0 ? name : id,
            Command: command,
            WorkingDirectory: null,
            Icon: null,
            TabTitle: null,
            Hidden: false,
            ProbeId: null,
            VisualsOrNull: null);

    [Fact]
    public void Ctor_FiresInitialEvent_UserOnlyCompose()
    {
        var src = new FakeProfileConfigSource
        {
            ParsedProfiles = new Dictionary<string, ProfileDef>
            {
                ["a"] = UserDef("a", "A"),
                ["b"] = UserDef("b", "B"),
            },
            DefaultProfileId = "a",
        };

        using var registry = new ProfileRegistry(
            src,
            EmptyDiscovery(),
            SynchronousDispatcher,
            NullLogger<ProfileRegistry>.Instance);

        // Post-ctor, the registry has already done an initial synchronous
        // compose (discovery is still running). We verify via direct state
        // reads; the Ctor fires events synchronously before returning so
        // subscribers that only need "subsequent changes" add their handler
        // after construction.
        Assert.True(registry.Version >= 1);
        Assert.Equal(2, registry.Profiles.Count);
        Assert.Equal("a", registry.DefaultProfileId);
    }

    [Fact]
    public async Task DiscoveryCompletes_FiresSecondEvent_WithDiscovered()
    {
        var src = new FakeProfileConfigSource
        {
            ParsedProfiles = new Dictionary<string, ProfileDef>
            {
                ["user-a"] = UserDef("user-a", "User A"),
            },
            DefaultProfileId = "user-a",
        };

        var tcs = new TaskCompletionSource<IReadOnlyList<DiscoveredProfile>>();
        Func<bool, CancellationToken, Task<IReadOnlyList<DiscoveredProfile>>> deferred =
            (_, _) => tcs.Task;

        var events = new List<int>();
        using var registry = new ProfileRegistry(
            src, deferred, SynchronousDispatcher, NullLogger<ProfileRegistry>.Instance);
        registry.ProfilesChanged += r => events.Add(r.Profiles.Count);

        // Version is 1, Profiles has only the user entry.
        Assert.Equal(1L, registry.Version);
        Assert.Single(registry.Profiles);

        // Complete discovery with one discovered profile.
        tcs.SetResult(new List<DiscoveredProfile>
        {
            new(Id: "wsl-ubuntu", Name: "Ubuntu", Command: "wsl.exe",
                ProbeId: "wsl", WorkingDirectory: null, Icon: null, TabTitle: null),
        });

        // Give the continuation a chance to run.
        await Task.Yield();
        for (int i = 0; i < 20 && registry.Version < 2; i++) await Task.Delay(5);

        Assert.Equal(2L, registry.Version);
        Assert.Equal(2, registry.Profiles.Count);
        Assert.Single(events);
        Assert.Equal(2, events[0]);
    }
}
