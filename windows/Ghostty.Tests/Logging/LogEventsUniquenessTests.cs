using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Ghostty.Tests.Logging;

/// <summary>
/// Enforces the hand-written invariant documented in both LogEvents.cs
/// files: "Each id appears in exactly two places: the definition here
/// and one [LoggerMessage(EventId = ...)] attribute." When someone
/// renames an event or forgets to migrate a call site, this test fires
/// before the drift lands on main.
/// </summary>
public class LogEventsUniquenessTests
{
    [Fact]
    public void EachEventIdConstant_IsReferenced_ExactlyOnce_OutsideItsDefinition()
    {
        var windowsRoot = FindWindowsSourceRoot();

        var coreEventsFile = Path.Combine(windowsRoot, "Ghostty.Core", "Logging", "LogEvents.cs");
        var shellEventsFile = Path.Combine(windowsRoot, "Ghostty", "Logging", "LogEvents.cs");

        Assert.True(File.Exists(coreEventsFile), $"Core LogEvents.cs not found at {coreEventsFile}");
        Assert.True(File.Exists(shellEventsFile), $"Shell LogEvents.cs not found at {shellEventsFile}");

        // { "Clipboard.ReadFailed", "Config.ReloadFailed", ... }
        var qualifiedNames = new List<string>();
        qualifiedNames.AddRange(CollectQualifiedConstants(coreEventsFile));
        qualifiedNames.AddRange(CollectQualifiedConstants(shellEventsFile));
        Assert.NotEmpty(qualifiedNames);

        var definitionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            coreEventsFile,
            shellEventsFile,
        };

        // Only scan production projects: Ghostty.Tests legitimately
        // references the same constant multiple times (one per assertion
        // that a given call site emitted the expected EventId), and
        // those references are not drift — they are the safety net.
        var ghosttyDir = Path.Combine(windowsRoot, "Ghostty");
        var coreDir = Path.Combine(windowsRoot, "Ghostty.Core");
        var allCsFiles = Directory.EnumerateFiles(ghosttyDir, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.Combine("obj", ""), StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains(Path.Combine("bin", ""), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var failures = new List<string>();
        foreach (var qualified in qualifiedNames)
        {
            // Match ".Foo.Bar" so substrings like "FooBar" inside an
            // unrelated identifier don't count. Every [LoggerMessage]
            // reference is "Ghostty.(Core.)Logging.LogEvents.Class.Name"
            // which ends in the same ".Class.Name" suffix we collected.
            var needle = "." + qualified;
            int usageCount = 0;
            foreach (var file in allCsFiles)
            {
                if (definitionFiles.Contains(file)) continue;
                var text = File.ReadAllText(file);
                if (text.Contains(needle, StringComparison.Ordinal))
                    usageCount++;
            }

            if (usageCount != 1)
            {
                failures.Add($"  {qualified}: found {usageCount} references outside definition (expected 1)");
            }
        }

        Assert.True(failures.Count == 0,
            "LogEvents constant(s) violate the one-reference-per-definition invariant:\n"
            + string.Join("\n", failures));
    }

    // Collect "ClassName.ConstantName" strings from the nested-class
    // LogEvents shape used in the codebase: one `internal static class`
    // per component range, each holding several `public const int`s.
    private static IEnumerable<string> CollectQualifiedConstants(string path)
    {
        var src = File.ReadAllText(path);
        // Strip block comments before scanning so inline code samples in
        // docstrings aren't mistaken for definitions.
        src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        string? currentClass = null;
        foreach (var rawLine in src.Split('\n'))
        {
            var line = rawLine.Trim();
            var classMatch = Regex.Match(line, @"internal\s+static\s+class\s+(\w+)");
            if (classMatch.Success && classMatch.Groups[1].Value != "LogEvents")
            {
                currentClass = classMatch.Groups[1].Value;
                continue;
            }

            if (currentClass is null) continue;

            var constMatch = Regex.Match(line, @"public\s+const\s+int\s+(\w+)\s*=");
            if (constMatch.Success)
            {
                yield return currentClass + "." + constMatch.Groups[1].Value;
            }
        }
    }

    // Walk up from the test's bin directory until we find the windows/
    // folder by looking for the solution file. Tests run from
    // windows/Ghostty.Tests/bin/<platform>/<config>/<tfm>/, so the
    // marker lives a few levels up.
    private static string FindWindowsSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Ghostty.sln");
            if (File.Exists(candidate))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate windows/Ghostty.sln by walking up from " + AppContext.BaseDirectory);
    }
}
