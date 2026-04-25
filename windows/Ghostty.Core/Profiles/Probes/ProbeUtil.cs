namespace Ghostty.Core.Profiles.Probes;

internal static class ProbeUtil
{
    /// <summary>
    /// Wrap a path in double quotes when it contains a space, so that
    /// libghostty's ArgIteratorGeneral (which is the parser downstream
    /// of config.command = .shell) keeps the path as a single argv
    /// token. Unquoted paths with spaces produce arg vectors like
    /// ["C:\Program", "Files\Git\bin\bash.exe", ...] and
    /// CreateProcessW fails.
    /// Already-quoted strings are returned unchanged.
    /// </summary>
    internal static string QuoteIfNeeded(string path)
        => path.Length > 0 && path.Contains(' ') && !path.StartsWith('"')
            ? "\"" + path + "\""
            : path;
}
