using System.Collections.Generic;
using System.Linq;
using Ghostty.Core.Profiles;
using Xunit;

namespace Ghostty.Tests.Profiles;

public sealed class ProfileOrderResolverTests
{
    private static ProfileDef User(string id, bool hidden = false)
        => new(Id: id, Name: id, Command: $"{id}.exe", Hidden: hidden);

    private static DiscoveredProfile Disc(string id, string probe = "test")
        => new(Id: id, Name: id, Command: $"{id}.exe", ProbeId: probe);

    [Fact]
    public void Resolve_NoProfileOrder_UserFirstThenDiscoveredAlpha()
    {
        var users = new[] { User("alpha"), User("zulu") };
        var discovered = new[] { Disc("delta"), Disc("bravo") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: discovered,
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Equal(new[] { "alpha", "zulu", "bravo", "delta" }, resolved.Select(r => r.Id));
    }

    [Fact]
    public void Resolve_UserAndDiscoveredSameId_UserWins()
    {
        var users = new[] { User("pwsh") with { Name = "MyPwsh" } };
        var discovered = new[] { Disc("pwsh") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: discovered,
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Single(resolved);
        Assert.Equal("MyPwsh", resolved[0].Name);
        Assert.Null(resolved[0].ProbeId);
    }

    [Fact]
    public void Resolve_HiddenProfile_OmittedFromResult()
    {
        var users = new[] { User("a"), User("b", hidden: true), User("c") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Equal(new[] { "a", "c" }, resolved.Select(r => r.Id));
    }

    [Fact]
    public void Resolve_HiddenSet_OverridesNonHiddenDef()
    {
        var users = new[] { User("a"), User("b") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string> { "a" }).ToList();

        Assert.Equal(new[] { "b" }, resolved.Select(r => r.Id));
    }

    [Fact]
    public void Resolve_ProfileOrder_ListedFirstThenUnlistedByDefault()
    {
        var users = new[] { User("alpha"), User("zulu") };
        var discovered = new[] { Disc("bravo"), Disc("delta") };
        var order = new[] { "delta", "alpha" };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: discovered,
            profileOrder: order,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Equal(new[] { "delta", "alpha", "zulu", "bravo" }, resolved.Select(r => r.Id));
    }

    [Fact]
    public void Resolve_ProfileOrderHasUnknownIds_SilentlyIgnoresThem()
    {
        var users = new[] { User("a") };
        var order = new[] { "ghost", "a", "phantom" };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: order,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Equal(new[] { "a" }, resolved.Select(r => r.Id));
    }

    [Fact]
    public void Resolve_DefaultProfileId_MarkedIsDefault()
    {
        var users = new[] { User("alpha"), User("zulu") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: "zulu",
            hidden: new HashSet<string>()).ToList();

        Assert.False(resolved.Single(r => r.Id == "alpha").IsDefault);
        Assert.True(resolved.Single(r => r.Id == "zulu").IsDefault);
    }

    [Fact]
    public void Resolve_DefaultProfileIdMissing_FallsBackToFirst()
    {
        var users = new[] { User("alpha"), User("zulu") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: "ghost",
            hidden: new HashSet<string>()).ToList();

        Assert.True(resolved[0].IsDefault);
        Assert.Equal("alpha", resolved[0].Id);
    }

    [Fact]
    public void Resolve_DefaultProfileIdNull_FirstIsDefault()
    {
        var users = new[] { User("alpha") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.True(resolved[0].IsDefault);
    }

    [Fact]
    public void Resolve_OrderIndex_ContiguousFromZero()
    {
        var users = new[] { User("a"), User("b"), User("c") };

        var resolved = ProfileOrderResolver.Resolve(
            user: users,
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).ToList();

        Assert.Equal(0, resolved[0].OrderIndex);
        Assert.Equal(1, resolved[1].OrderIndex);
        Assert.Equal(2, resolved[2].OrderIndex);
    }

    [Fact]
    public void Resolve_DiscoveredProfile_HasProbeIdSet()
    {
        var resolved = ProfileOrderResolver.Resolve(
            user: System.Array.Empty<ProfileDef>(),
            discovered: new[] { Disc("wsl-ubuntu", probe: "wsl") },
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>()).Single();

        Assert.Equal("wsl", resolved.ProbeId);
    }

    [Fact]
    public void Resolve_EmptyInputs_ReturnsEmpty()
    {
        var resolved = ProfileOrderResolver.Resolve(
            user: System.Array.Empty<ProfileDef>(),
            discovered: System.Array.Empty<DiscoveredProfile>(),
            profileOrder: null,
            defaultProfileId: null,
            hidden: new HashSet<string>());

        Assert.Empty(resolved);
    }
}
