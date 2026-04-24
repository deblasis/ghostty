using System.Linq;
using System.Threading;
using Ghostty.Core.Profiles.Probes;
using Ghostty.Tests.Profiles.Scenarios;
using Xunit;

namespace Ghostty.Tests.Profiles.Scenarios;

public sealed class ScenarioProbeTests
{
    [Fact]
    public async System.Threading.Tasks.Task EmptyMachine_ProducesOnlyCmd()
    {
        var s = ScenarioLoader.Load("EmptyMachine");
        var cmd = await new CmdProbe(s.FileSystem).DiscoverAsync(CancellationToken.None);
        var pwsh = await new PowerShellProbe(s.FileSystem, s.ProcessRunner).DiscoverAsync(CancellationToken.None);
        var wsl = await new WslProbe(s.ProcessRunner).DiscoverAsync(CancellationToken.None);
        var git = await new GitBashProbe(s.Registry, s.FileSystem).DiscoverAsync(CancellationToken.None);
        var az = await new AzureCloudShellProbe(s.ProcessRunner).DiscoverAsync(CancellationToken.None);

        Assert.Single(cmd);
        Assert.Empty(pwsh);
        Assert.Empty(wsl);
        Assert.Empty(git);
        Assert.Empty(az);
    }

    [Fact]
    public async System.Threading.Tasks.Task HeavyDevMachine_AllProbesReturnSomething()
    {
        var s = ScenarioLoader.Load("HeavyDevMachine");
        var cmd = await new CmdProbe(s.FileSystem).DiscoverAsync(CancellationToken.None);
        var pwsh = await new PowerShellProbe(s.FileSystem, s.ProcessRunner).DiscoverAsync(CancellationToken.None);
        var wsl = await new WslProbe(s.ProcessRunner).DiscoverAsync(CancellationToken.None);
        var git = await new GitBashProbe(s.Registry, s.FileSystem).DiscoverAsync(CancellationToken.None);
        var az = await new AzureCloudShellProbe(s.ProcessRunner).DiscoverAsync(CancellationToken.None);

        Assert.Single(cmd);
        Assert.Equal(2, pwsh.Count); // pwsh-7 + pwsh-windows
        Assert.Equal(3, wsl.Count);  // Ubuntu-22.04, Debian, kali-linux
        Assert.Single(git);
        Assert.Single(az);
    }

    [Fact]
    public async System.Threading.Tasks.Task CorruptWslOutput_SkipsGarbledReturnsUbuntu()
    {
        var s = ScenarioLoader.Load("CorruptWslOutput");
        var wsl = await new WslProbe(s.ProcessRunner).DiscoverAsync(CancellationToken.None);
        var p = Assert.Single(wsl);
        Assert.Equal("wsl-ubuntu", p.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task RegistryMissing_GitBashNotDiscoveredEvenThoughBashExeExists()
    {
        var s = ScenarioLoader.Load("RegistryMissing");
        var git = await new GitBashProbe(s.Registry, s.FileSystem).DiscoverAsync(CancellationToken.None);
        Assert.Empty(git);
    }
}
