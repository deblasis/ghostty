using Ghostty.Core.Profiles;
using Xunit;

namespace Ghostty.Tests.Profiles;

public sealed class ProfileSourceParserTests
{
    [Fact]
    public void Parse_SingleProfile_AllRequiredKeys()
    {
        const string config = """
            profile.pwsh.name = PowerShell
            profile.pwsh.command = pwsh.exe
            """;

        var result = ProfileSourceParser.Parse(config);

        Assert.Single(result.Profiles);
        var p = result.Profiles["pwsh"];
        Assert.Equal("pwsh", p.Id);
        Assert.Equal("PowerShell", p.Name);
        Assert.Equal("pwsh.exe", p.Command);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_MultipleProfiles_AllReturned()
    {
        const string config = """
            profile.pwsh.name = PowerShell
            profile.pwsh.command = pwsh.exe
            profile.cmd.name = Command Prompt
            profile.cmd.command = cmd.exe
            """;

        var result = ProfileSourceParser.Parse(config);

        Assert.Equal(2, result.Profiles.Count);
        Assert.True(result.Profiles.ContainsKey("pwsh"));
        Assert.True(result.Profiles.ContainsKey("cmd"));
    }

    [Fact]
    public void Parse_VisualOverrides_PopulatedOnVisuals()
    {
        const string config = """
            profile.pwsh.name = PowerShell
            profile.pwsh.command = pwsh.exe
            profile.pwsh.theme = GruvboxDark
            profile.pwsh.background-opacity = 0.85
            profile.pwsh.font-family = CaskaydiaCove Nerd Font
            profile.pwsh.font-size = 13.5
            profile.pwsh.cursor-style = block
            """;

        var p = ProfileSourceParser.Parse(config).Profiles["pwsh"];

        Assert.Equal("GruvboxDark", p.Visuals.Theme);
        Assert.Equal(0.85, p.Visuals.BackgroundOpacity);
        Assert.Equal("CaskaydiaCove Nerd Font", p.Visuals.FontFamily);
        Assert.Equal(13.5, p.Visuals.FontSize);
        Assert.Equal("block", p.Visuals.CursorStyle);
    }

    [Fact]
    public void Parse_OptionalKeys_PopulateOnDef()
    {
        const string config = """
            profile.wsl.name = WSL Ubuntu
            profile.wsl.command = wsl -d Ubuntu
            profile.wsl.working-directory = /home/me
            profile.wsl.tab-title = Ubuntu
            profile.wsl.hidden = false
            """;

        var p = ProfileSourceParser.Parse(config).Profiles["wsl"];

        Assert.Equal("/home/me", p.WorkingDirectory);
        Assert.Equal("Ubuntu", p.TabTitle);
        Assert.False(p.Hidden);
    }

    [Fact]
    public void Parse_IgnoresNonProfileLines()
    {
        const string config = """
            font-family = Cascadia Code
            theme = GruvboxDark
            profile.pwsh.name = PowerShell
            profile.pwsh.command = pwsh.exe
            background-opacity = 0.9
            """;

        var result = ProfileSourceParser.Parse(config);

        Assert.Single(result.Profiles);
        Assert.True(result.Profiles.ContainsKey("pwsh"));
    }

    [Fact]
    public void Parse_HiddenTrueInterpretedCorrectly()
    {
        const string config = """
            profile.wsl-debian.name = Debian
            profile.wsl-debian.command = wsl -d Debian
            profile.wsl-debian.hidden = true
            """;

        var p = ProfileSourceParser.Parse(config).Profiles["wsl-debian"];

        Assert.True(p.Hidden);
    }
}
