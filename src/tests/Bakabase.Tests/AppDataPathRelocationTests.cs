using System.Collections.Generic;
using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class AppDataPathRelocationTests
{
    private const string Current = "/install/current/AppData";
    private const string PreVelopack = "/install/AppData";
    private const string CustomMoved = "/elsewhere/AppData";

    private static IEnumerable<string?> NoOldRoots() => [];
    private static IEnumerable<string?> WithVelopackStrip() => [AppDataPathRelocation.TryStripCurrentSegment(Current)];
    private static IEnumerable<string?> WithLastObserved(string? lastObserved) => [lastObserved];

    // ─── Resolve ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Resolve_Null_ReturnsNull()
    {
        Assert.IsNull(AppDataPathRelocation.Resolve(null, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Resolve_Empty_ReturnsEmpty()
    {
        Assert.AreEqual("", AppDataPathRelocation.Resolve("", Current, NoOldRoots()));
    }

    [TestMethod]
    public void Resolve_HttpUrl_Unchanged()
    {
        const string url = "https://cdn.example.com/cover.jpg";
        Assert.AreEqual(url, AppDataPathRelocation.Resolve(url, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Resolve_RelativePath_ResolvesAgainstCurrent()
    {
        var resolved = AppDataPathRelocation.Resolve("data/covers/manual/1-0.jpg", Current, NoOldRoots());
        Assert.AreEqual("/install/current/AppData/data/covers/manual/1-0.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_AbsoluteUnderCurrent_NormalizedAndUnchanged()
    {
        var input = "/install/current/AppData/data/covers/manual/1-0.jpg";
        Assert.AreEqual(input, AppDataPathRelocation.Resolve(input, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Resolve_AbsoluteUnderVelopackStrippedRoot_RebasesToCurrent()
    {
        var input = "/install/AppData/data/covers/manual/1-0.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, Current, WithVelopackStrip());
        Assert.AreEqual("/install/current/AppData/data/covers/manual/1-0.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_AbsoluteUnderLastObserved_RebasesToCurrent()
    {
        var input = "/elsewhere/AppData/data/enhancers/Bangumi/9/cover.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, Current, WithLastObserved(CustomMoved));
        Assert.AreEqual("/install/current/AppData/data/enhancers/Bangumi/9/cover.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_AbsoluteOnUserDisk_Unchanged()
    {
        var input = "/Users/foo/Movies/A/cover.jpg";
        Assert.AreEqual(input, AppDataPathRelocation.Resolve(input, Current, WithLastObserved(CustomMoved)));
    }

    [TestMethod]
    public void Resolve_LongerPrefixWins()
    {
        // Two candidate roots that overlap: short one is "/a/b", long one is "/a/b/c".
        // Stored path under the longer root must NOT be rebased via the shorter one.
        const string current = "/a/b/c";
        var input = "/a/b/c/data/covers/local/1.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, current, ["/a/b"]);
        Assert.AreEqual("/a/b/c/data/covers/local/1.jpg", resolved); // matches current exactly, no rebase
    }

    [TestMethod]
    public void Resolve_LongerPrefixWins_RebasePicksLongest()
    {
        // current = /new
        // old roots overlap: /a/b and /a/b/c. Path is under /a/b/c.
        // Must strip /a/b/c (longer) → relative "data/...", NOT strip /a/b → relative "c/data/...".
        const string current = "/new";
        var input = "/a/b/c/data/foo.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, current, ["/a/b", "/a/b/c"]);
        Assert.AreEqual("/new/data/foo.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_PrefixBoundaryRespected_NoFalseMatch()
    {
        // "/a/b" must NOT match "/a/bc/...".
        const string current = "/new";
        var input = "/a/bc/foo.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, current, ["/a/b"]);
        Assert.AreEqual("/a/bc/foo.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_WindowsDrivePathWithBackslashes_Normalized()
    {
        // Realistic Windows DB scenario: paths use backslashes and have a drive letter.
        var input = @"C:\Bakabase\AppData\data\covers\local\7.jpg";
        var resolved = AppDataPathRelocation.Resolve(input,
            "C:/Bakabase/current/AppData",
            [AppDataPathRelocation.TryStripCurrentSegment("C:/Bakabase/current/AppData")]);
        Assert.AreEqual("C:/Bakabase/current/AppData/data/covers/local/7.jpg", resolved);
    }

    [TestMethod]
    public void Resolve_AbsoluteEqualsRoot_ReturnsCurrent()
    {
        var resolved = AppDataPathRelocation.Resolve(PreVelopack, Current, [PreVelopack]);
        Assert.AreEqual(Current, resolved);
    }

    // ─── Relativize ───────────────────────────────────────────────────────

    [TestMethod]
    public void Relativize_Null_ReturnsNull()
    {
        Assert.IsNull(AppDataPathRelocation.Relativize(null, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Relativize_HttpsUrl_Unchanged()
    {
        const string url = "https://cdn.example.com/cover.jpg";
        Assert.AreEqual(url, AppDataPathRelocation.Relativize(url, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Relativize_AlreadyRelative_Unchanged()
    {
        Assert.AreEqual("data/covers/manual/1-0.jpg",
            AppDataPathRelocation.Relativize("data/covers/manual/1-0.jpg", Current, NoOldRoots()));
    }

    [TestMethod]
    public void Relativize_AbsoluteUnderCurrent_ReturnsRelative()
    {
        var input = "/install/current/AppData/data/covers/manual/1-0.jpg";
        Assert.AreEqual("data/covers/manual/1-0.jpg",
            AppDataPathRelocation.Relativize(input, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Relativize_AbsoluteUnderOldRoot_ReturnsRelative()
    {
        var input = "/install/AppData/data/covers/manual/1-0.jpg";
        Assert.AreEqual("data/covers/manual/1-0.jpg",
            AppDataPathRelocation.Relativize(input, Current, WithVelopackStrip()));
    }

    [TestMethod]
    public void Relativize_AbsoluteOnUserDisk_Unchanged()
    {
        var input = "/Users/foo/Movies/A/cover.jpg";
        Assert.AreEqual(input,
            AppDataPathRelocation.Relativize(input, Current, WithLastObserved(CustomMoved)));
    }

    [TestMethod]
    public void Relativize_LongerPrefixWins()
    {
        // Two roots: /a/b and /a/b/c. Path is under /a/b/c.
        // Longer must win — relative should be "data/foo.jpg", not "c/data/foo.jpg".
        var input = "/a/b/c/data/foo.jpg";
        Assert.AreEqual("data/foo.jpg",
            AppDataPathRelocation.Relativize(input, "/new", ["/a/b", "/a/b/c"]));
    }

    [TestMethod]
    public void Relativize_CaseInsensitivePrefix_StillMatches()
    {
        // Stored absolute path uses different casing for the AppData root segment.
        var input = "/INSTALL/AppData/data/covers/local/3.jpg";
        Assert.AreEqual("data/covers/local/3.jpg",
            AppDataPathRelocation.Relativize(input, Current, [PreVelopack]));
    }

    [TestMethod]
    public void Relativize_PrefixBoundaryRespected_NoFalseMatch()
    {
        var input = "/install/AppDataExtra/file.jpg";
        Assert.AreEqual(input,
            AppDataPathRelocation.Relativize(input, Current, [PreVelopack]));
    }

    [TestMethod]
    public void Relativize_AbsoluteEqualsRoot_ReturnsEmpty()
    {
        // Edge case — path is exactly the AppData root with no suffix.
        Assert.AreEqual("",
            AppDataPathRelocation.Relativize("/install/AppData", Current, [PreVelopack]));
    }

    // ─── TryStripCurrentSegment ───────────────────────────────────────────

    [TestMethod]
    public void StripCurrentSegment_VelopackPath_RemovesCurrent()
    {
        Assert.AreEqual("/install/AppData",
            AppDataPathRelocation.TryStripCurrentSegment("/install/current/AppData"));
    }

    [TestMethod]
    public void StripCurrentSegment_NoCurrentSegment_ReturnsNull()
    {
        Assert.IsNull(AppDataPathRelocation.TryStripCurrentSegment("/install/AppData"));
    }

    [TestMethod]
    public void StripCurrentSegment_CaseInsensitive()
    {
        Assert.AreEqual("/install/AppData",
            AppDataPathRelocation.TryStripCurrentSegment("/install/Current/AppData"));
    }

    [TestMethod]
    public void StripCurrentSegment_OnlyStripsBeforeAppData_NotElsewhere()
    {
        // "current" segment NOT immediately before AppData should not be stripped.
        Assert.IsNull(AppDataPathRelocation.TryStripCurrentSegment("/install/current/Other/foo"));
    }

    [TestMethod]
    public void StripCurrentSegment_StripsLastOccurrence()
    {
        // If somehow there are multiple "/current/AppData" occurrences, strip the LAST one.
        Assert.AreEqual("/foo/current/AppData/sub/AppData",
            AppDataPathRelocation.TryStripCurrentSegment("/foo/current/AppData/sub/current/AppData"));
    }

    // ─── Round-trip ───────────────────────────────────────────────────────

    [TestMethod]
    public void RoundTrip_RelativizeThenResolve_RecoverInputUnderAnyRoot()
    {
        var input = "/install/AppData/data/covers/manual/1-0.jpg";
        var rel = AppDataPathRelocation.Relativize(input, Current, WithVelopackStrip());
        var back = AppDataPathRelocation.Resolve(rel, Current, NoOldRoots());
        // Resolves to current root, not original old root.
        Assert.AreEqual("/install/current/AppData/data/covers/manual/1-0.jpg", back);
    }

    [TestMethod]
    public void RoundTrip_ResolveThenRelativize_NormalizesToRelative()
    {
        var input = "/install/AppData/data/covers/manual/1-0.jpg";
        var resolved = AppDataPathRelocation.Resolve(input, Current, WithVelopackStrip());
        var rel = AppDataPathRelocation.Relativize(resolved, Current, NoOldRoots());
        Assert.AreEqual("data/covers/manual/1-0.jpg", rel);
    }

    [TestMethod]
    public void Idempotent_ResolveTwice()
    {
        var input = "/install/AppData/data/x.jpg";
        var once = AppDataPathRelocation.Resolve(input, Current, WithVelopackStrip());
        var twice = AppDataPathRelocation.Resolve(once, Current, WithVelopackStrip());
        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void Idempotent_RelativizeTwice()
    {
        var input = "/install/AppData/data/x.jpg";
        var once = AppDataPathRelocation.Relativize(input, Current, WithVelopackStrip());
        var twice = AppDataPathRelocation.Relativize(once, Current, WithVelopackStrip());
        Assert.AreEqual(once, twice);
    }

    // ─── Path-shape guard ─────────────────────────────────────────────────

    [TestMethod]
    public void Resolve_OpaqueToken_PassesThroughUnchanged()
    {
        // UUIDs / choice IDs don't contain a separator and must not be prepended with AppData root.
        const string uuid = "12345678-abcd-1234-abcd-123456789012";
        Assert.AreEqual(uuid, AppDataPathRelocation.Resolve(uuid, Current, NoOldRoots()));
    }

    [TestMethod]
    public void Resolve_BareFilename_PassesThroughUnchanged()
    {
        // No separator → not treated as an AppData-relative path.
        Assert.AreEqual("cover.jpg", AppDataPathRelocation.Resolve("cover.jpg", Current, NoOldRoots()));
    }

    [TestMethod]
    public void Relativize_OpaqueToken_PassesThroughUnchanged()
    {
        const string uuid = "12345678-abcd-1234-abcd-123456789012";
        Assert.AreEqual(uuid, AppDataPathRelocation.Relativize(uuid, Current, NoOldRoots()));
    }

    // ─── Candidate root building ──────────────────────────────────────────

    [TestMethod]
    public void BuildCandidateRoots_DedupsAndSortsByLengthDesc()
    {
        var roots = AppDataPathRelocation.BuildCandidateRoots(Current,
            [PreVelopack, Current, "/elsewhere/AppData/sub", "/elsewhere/AppData", null, ""]);
        // Expected sorted: longest first; current is explicitly included; null/empty dropped; dupes removed.
        var lengths = new List<int>();
        foreach (var r in roots) lengths.Add(r.Length);
        for (var i = 1; i < lengths.Count; i++)
        {
            Assert.IsTrue(lengths[i] <= lengths[i - 1], "Roots not sorted descending by length");
        }
        Assert.IsTrue(roots.Count >= 4, "Expected at least 4 roots after dedup");
    }
}
