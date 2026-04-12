// Equivalence test: reference raw-vtable path (baseline) vs
// migrated CsWin32 path (production). Fails if the two lists
// diverge in ANY family name, which catches locale-index drift,
// vtable-slot mistakes, and PWSTR truncation bugs that a weak
// "Count > 20" sanity test would silently miss.

using System;
using System.Collections.Generic;
using System.Linq;
using Ghostty.Core.DirectWrite;
using Xunit;

namespace Ghostty.Tests.Interop;

public sealed class DWriteFontFamilyEquivalenceTest
{
    // Skipped until Task 7 Step 3 lands EnumerateMigrated. The
    // migrated call below is commented out so this file compiles
    // against the Task 1 scaffold (reference path only).
    [Fact(Skip = "migrated path lands in Task 7")]
    public void ReferenceAndMigratedMatch()
    {
        var reference = DWriteFontEnumerator.EnumerateReference();
        // var migrated = DWriteFontEnumerator.EnumerateMigrated();

        // Both paths must see Segoe UI on any Windows 10/11 host.
        Assert.Contains("Segoe UI", reference, StringComparer.OrdinalIgnoreCase);
        // Assert.Contains("Segoe UI", migrated, StringComparer.OrdinalIgnoreCase);

        // Hard equivalence: any divergence is a migration regression.
        // Assert.True(
        //     reference.SequenceEqual(migrated, StringComparer.OrdinalIgnoreCase),
        //     BuildDiffMessage(reference, migrated));
    }

    private static string BuildDiffMessage(IList<string> a, IList<string> b)
    {
        var onlyInA = a.Except(b, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInB = b.Except(a, StringComparer.OrdinalIgnoreCase).ToList();
        return $"Font lists differ. ref_only={string.Join(",", onlyInA)} " +
               $"mig_only={string.Join(",", onlyInB)}";
    }
}
