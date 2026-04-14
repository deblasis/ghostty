using System;
using System.Collections.Generic;
using System.Linq;

namespace Ghostty.Core.Settings;

/// <summary>
/// Which tier of match an entry produced. Higher enum value == better
/// match; results sort by this descending. None is used internally and
/// never surfaces in <see cref="SearchHit"/>.
///
/// Tier order is set by the Phase 3 spec; see
/// docs/superpowers/specs/2026-04-14-settings-reorganization-design.md.
/// </summary>
public enum MatchKind
{
    None = 0,
    FuzzyKey = 1,
    TagContains = 2,
    TagExact = 3,
    DescriptionContains = 4,
    LabelContains = 5,
    LabelPrefix = 6,
    ExactLabel = 7,
}

public sealed record SearchHit(SettingsEntry Entry, MatchKind Kind);

/// <summary>
/// Tiered substring + fuzzy search over <see cref="SettingsIndex"/>.
/// Lives in Ghostty.Core so the ranking is unit-tested without pulling
/// WinUI 3 into the test assembly.
/// </summary>
public static class SettingsSearch
{
    public static IReadOnlyList<SearchHit> Search(
        string? query,
        IEnumerable<SettingsEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
        var q = query.Trim().ToLowerInvariant();

        var hits = new List<(int InputIndex, SearchHit Hit)>();
        int i = 0;
        foreach (var entry in entries)
        {
            var kind = Classify(entry, q);
            if (kind != MatchKind.None)
                hits.Add((i, new SearchHit(entry, kind)));
            i++;
        }

        // Stable sort: tier desc, then original input order within a tier
        // so UI grouping (by page/section) stays predictable.
        return hits
            .OrderByDescending(x => (int)x.Hit.Kind)
            .ThenBy(x => x.InputIndex)
            .Select(x => x.Hit)
            .ToList();
    }

    private static MatchKind Classify(SettingsEntry e, string q)
    {
        var label = e.Label.ToLowerInvariant();
        if (label == q) return MatchKind.ExactLabel;
        if (label.StartsWith(q, StringComparison.Ordinal)) return MatchKind.LabelPrefix;
        if (label.Contains(q, StringComparison.Ordinal)) return MatchKind.LabelContains;

        if (e.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            return MatchKind.DescriptionContains;

        var tagExact = false;
        var tagContains = false;
        foreach (var tag in e.Tags)
        {
            var t = tag.ToLowerInvariant();
            if (t == q) { tagExact = true; break; }
            if (t.Contains(q, StringComparison.Ordinal)) tagContains = true;
        }
        if (tagExact) return MatchKind.TagExact;
        if (tagContains) return MatchKind.TagContains;

        if (IsSubsequence(q, e.Key.ToLowerInvariant())) return MatchKind.FuzzyKey;

        return MatchKind.None;
    }

    // Characters of needle appear in haystack in order (not necessarily
    // contiguous). Classic fuzzy-finder primitive, kept under the spec's
    // "under 50 lines" bar.
    private static bool IsSubsequence(string needle, string haystack)
    {
        if (needle.Length == 0) return true;
        int ni = 0;
        foreach (var c in haystack)
        {
            if (c == needle[ni])
            {
                ni++;
                if (ni == needle.Length) return true;
            }
        }
        return false;
    }
}
