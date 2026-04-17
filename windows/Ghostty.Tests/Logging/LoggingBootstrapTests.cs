using Ghostty.Core.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ghostty.Tests.Logging;

public class LoggingBootstrapTests
{
    [Fact]
    public void ParseFilterOptions_DefaultsToInformation_WhenLevelEmpty()
    {
        var opts = LoggingBootstrap.ParseFilterOptions(logLevel: null, logFilter: null);

        Assert.Equal(LogLevel.Information, opts.MinLevel);
        Assert.Empty(opts.Rules);
    }

    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("information", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("off", LogLevel.None)]
    [InlineData("none", LogLevel.None)]
    public void ParseFilterOptions_ParsesLevelCaseInsensitively(string input, LogLevel expected)
    {
        var opts = LoggingBootstrap.ParseFilterOptions(input, null);
        Assert.Equal(expected, opts.MinLevel);
    }

    [Fact]
    public void ParseFilterOptions_UnknownLevel_FallsBackToInformation()
    {
        var opts = LoggingBootstrap.ParseFilterOptions("bogus", null);
        Assert.Equal(LogLevel.Information, opts.MinLevel);
    }

    [Fact]
    public void ParseFilterOptions_SingleFilterRule_IsApplied()
    {
        var opts = LoggingBootstrap.ParseFilterOptions(
            "info",
            "Ghostty.Services.ThemePreviewService=trace");

        Assert.Single(opts.Rules);
        Assert.Equal("Ghostty.Services.ThemePreviewService", opts.Rules[0].CategoryName);
        Assert.Equal(LogLevel.Trace, opts.Rules[0].LogLevel);
    }

    [Fact]
    public void ParseFilterOptions_MultipleCommaSeparatedRules_AreAllApplied()
    {
        var opts = LoggingBootstrap.ParseFilterOptions(
            "info",
            "Ghostty.Services.ThemePreviewService=trace, Ghostty.Core.Config=warn");

        Assert.Equal(2, opts.Rules.Count);
        Assert.Contains(opts.Rules, r =>
            r.CategoryName == "Ghostty.Services.ThemePreviewService" && r.LogLevel == LogLevel.Trace);
        Assert.Contains(opts.Rules, r =>
            r.CategoryName == "Ghostty.Core.Config" && r.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void ParseFilterOptions_MalformedPair_IsSkipped()
    {
        var opts = LoggingBootstrap.ParseFilterOptions(
            "info",
            "Ghostty.Services=warn, NoEqualsSign, =warn, Ghostty.Core=");

        Assert.Single(opts.Rules);
        Assert.Equal("Ghostty.Services", opts.Rules[0].CategoryName);
    }

    [Fact]
    public void ParseFilterOptions_UnknownLevelInRule_IsSkipped()
    {
        var opts = LoggingBootstrap.ParseFilterOptions(
            "info",
            "Ghostty.Services=bogus, Ghostty.Core=warn");

        Assert.Single(opts.Rules);
        Assert.Equal("Ghostty.Core", opts.Rules[0].CategoryName);
    }
}
