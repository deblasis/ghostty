using System.Text;

namespace Ghostty.Core.Version;

/// <summary>
/// Renders <see cref="VersionInfo"/> to either plain text (for the
/// command-palette dialog and for redirected stdout) or ANSI text (for
/// interactive stdout, which adds an OSC 8 hyperlink to the header).
/// </summary>
public static class VersionRenderer
{
    private const string CommitUrlPrefix = "https://github.com/deblasis/wintty/commit/";
    private const string Osc8Open = "\x1b]8;;";
    private const string Osc8Close = "\x1b]8;;\x1b\\";
    private const string St = "\x1b\\";

    /// <summary>Plain rendering. No escape sequences. Used by the dialog
    /// and by redirected stdout.</summary>
    public static string RenderPlain(VersionInfo info)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, info, ansi: false);
        AppendBody(sb, info);
        return sb.ToString();
    }

    /// <summary>ANSI rendering. Header is wrapped in an OSC 8 hyperlink
    /// to the commit on github.com. Used for interactive stdout.</summary>
    public static string RenderAnsi(VersionInfo info)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, info, ansi: true);
        AppendBody(sb, info);
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, VersionInfo info, bool ansi)
    {
        var hasCommit = !string.IsNullOrEmpty(info.WinttyCommit) && info.WinttyCommit != "unknown";
        if (ansi && hasCommit)
        {
            sb.Append(Osc8Open).Append(CommitUrlPrefix).Append(info.WinttyCommit).Append(St);
            sb.Append("Wintty ").Append(info.WinttyVersionString);
            sb.Append(Osc8Close);
            sb.Append('\n');
        }
        else
        {
            sb.Append("Wintty ").Append(info.WinttyVersionString).Append('\n');
        }
        if (hasCommit)
        {
            sb.Append("  ").Append(CommitUrlPrefix).Append(info.WinttyCommit).Append('\n');
        }
        sb.Append('\n');
    }

    private static void AppendBody(StringBuilder sb, VersionInfo info)
    {
        sb.Append("Version\n");
        Field(sb, "version",     info.WinttyVersion);
        Field(sb, "channel",     info.LibGhostty.Channel);
        Field(sb, "edition",     EditionLabel.Format(info.Edition));
        sb.Append('\n');

        sb.Append("Build Config\n");
        Field(sb, "Zig version",  info.LibGhostty.ZigVersion);
        Field(sb, ".NET runtime", info.DotnetRuntime);
        Field(sb, "app runtime",  info.AppRuntime);
        Field(sb, "renderer",     info.Renderer);
        Field(sb, "font engine",  info.FontEngine);
        Field(sb, "libghostty",   FormatLibghosttyVersion(info.LibGhostty));
        Field(sb, "windows",      info.WindowsVersion);
        Field(sb, "arch",         info.Architecture);
        Field(sb, "build mode",   info.MsbuildConfig);
    }

    private static string FormatLibghosttyVersion(LibGhosttyBuildInfo lib)
    {
        if (string.IsNullOrEmpty(lib.Commit)) return lib.Version;
        return $"{lib.Version}+{lib.Commit}";
    }

    private static void Field(StringBuilder sb, string label, string value)
    {
        // Two-space indent, label, ":", padded to column 18, then value.
        const int column = 18;
        sb.Append("  ").Append(label).Append(':');
        var padding = column - (2 + label.Length + 1);
        if (padding > 0) sb.Append(' ', padding);
        sb.Append(value).Append('\n');
    }
}
