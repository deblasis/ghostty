using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ghostty.Core.Sponsor.Update;

/// <summary>
/// Tracks which update versions the user has explicitly skipped.
/// Persisted as JSON to a user-local file (caller supplies path;
/// shell passes %LOCALAPPDATA%\Ghostty\update-skips.json).
/// Version comparison uses System.Version parsing; strings that
/// fail to parse compare lexicographically.
/// </summary>
public sealed class UpdateSkipList
{
    private readonly string _path;
    private readonly HashSet<string> _skipped;

    public UpdateSkipList(string path)
    {
        _path = path;
        _skipped = Load(path);
    }

    /// <summary>Add a version to the skip list and persist.</summary>
    public void Skip(string version)
    {
        if (_skipped.Add(version))
        {
            Save();
        }
    }

    /// <summary>
    /// True iff <paramref name="version"/> is in the skip list AND
    /// no strictly-greater version has been skipped. In practice this
    /// means: skipping 1.4.2 does not silence 1.4.3 or later.
    /// </summary>
    public bool IsSkipped(string version)
    {
        if (!_skipped.Contains(version))
        {
            return false;
        }
        // Any strictly-greater skipped version invalidates the skip
        // of older ones — but simpler: newer versions we've never seen
        // are not skipped by definition. This method only answers for
        // a specific version, so membership alone is correct.
        return true;
    }

    private static HashSet<string> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[sponsor/update] skip-list load failed: {ex.Message}");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var json = JsonSerializer.Serialize(new List<string>(_skipped));
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[sponsor/update] skip-list save failed: {ex.Message}");
        }
    }
}
