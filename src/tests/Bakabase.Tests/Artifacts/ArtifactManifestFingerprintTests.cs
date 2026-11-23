using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactManifestFingerprintTests
{
    private static string BufferedFingerprint(IEnumerable<ArtifactManifestEntry> entries, IReadOnlyList<string>? directories)
    {
        var files = JsonSerializer.Serialize(entries.Select(e => new[] {e.RelativePath, e.Sha256}).ToArray());
        return ArtifactManifestFiles.Hash(directories is {Count: > 0}
            ? "artifact-directories-v1\n" + files + "\n" +
              JsonSerializer.Serialize(directories.OrderBy(path => path, StringComparer.Ordinal).ToArray())
            : files);
    }

    [TestMethod]
    public void Both_formats_preserve_historical_order_escaping_and_nulls()
    {
        ArtifactManifestEntry[] entries =
        [
            new("z/原帖 \uD83D\uDC31", 42, "quoted\"hash"),
            new("a/\r\n\u0001/<&>\\", 0, "日本語"),
            new("unpaired/\ud800", -1, null!),
            new(new string('漢', 18_000), 0, "escaped entry larger than an output buffer"),
            new(null!, 1, "same path hashes keep their original position"),
            new("z/原帖 \uD83D\uDC31", long.MaxValue, "another hash")
        ];
        foreach (var files in new[] {Array.Empty<ArtifactManifestEntry>(), entries, entries.Reverse().ToArray()})
        foreach (var directories in new string[]?[] {null, [], ["漢字", "A", null!, "a", "A", "quoted\"\r\n"]})
        {
            Assert.AreEqual(BufferedFingerprint(files, directories), ArtifactManifestFiles.Fingerprint(files, directories));
            if (directories is null or {Length: 0})
                Assert.AreEqual(BufferedFingerprint(files, directories), ArtifactManifestFiles.Fingerprint(files));
        }
    }

    [TestMethod]
    public void Large_manifest_matches_old_bytes_without_allocating_a_manifest_sized_buffer()
    {
        const int count = 50_000;
        var name = new string('p', 96);
        var digest = new string('a', 64);
        var files = Enumerable.Range(0, count).Select(i => new ArtifactManifestEntry($"{name}/{i}.data", i, digest)).ToArray();
        string[] directories = ["second", "first"];
        var expected = BufferedFingerprint(files, directories);
        _ = ArtifactManifestFiles.Fingerprint(files.Take(1), directories);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = ArtifactManifestFiles.Fingerprint(files, directories);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(expected, actual);
        // Even the minimum serialized payload length excludes the pair structure and numeric suffixes.
        var minimumWireLength = (long)count * (Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(digest));
        Assert.IsTrue(allocated < minimumWireLength / 16, $"Allocated {allocated:N0} bytes for a payload of at least {minimumWireLength:N0} bytes.");
    }
}
