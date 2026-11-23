using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactManifestFilesTests
{
    private string _root = null!;
    private string _file = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "artifact-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "result.bin");
        await File.WriteAllTextAsync(_file, "verified-content");
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task Cached_path_timestamp_before_open_does_not_invalidate_a_stable_file()
    {
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddMinutes(-2));
        var cached = new FileInfo(_file);
        var staleWritten = cached.LastWriteTimeUtc;
        File.SetLastWriteTimeUtc(_file, staleWritten.AddMinutes(1));
        Assert.AreNotEqual(staleWritten, File.GetLastWriteTimeUtc(_file));
        Assert.AreEqual(staleWritten, cached.LastWriteTimeUtc);

        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file], _ => cached);

        Assert.AreEqual(1, manifest.Entries.Count);
        Assert.AreEqual(new FileInfo(_file).Length, manifest.Entries[0].Length);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_file))).ToLowerInvariant(),
            manifest.Entries[0].Sha256);
    }

    [TestMethod]
    public async Task Actual_write_time_change_after_open_is_still_rejected()
    {
        await Assert.ThrowsExceptionAsync<IOException>(() => ArtifactManifestFiles.CaptureAsync(_root, [_file],
            path => new FileInfo(path), async (stream, ct) =>
            {
                var hash = await SHA256.HashDataAsync(stream, ct);
                File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddMinutes(2));
                return hash;
            }));
    }

    [TestMethod]
    [DataRow("stable", null, 1)]
    [DataRow("timestamp", null, 2)]
    [DataRow("content", "content", 2)]
    [DataRow("replaced", "identity", 1)]
    [DataRow("replaced-preserving-mtime", "identity", 1)]
    [DataRow("size", "length", 1)]
    [DataRow("unstable", "mtime", 2)]
    public async Task Private_extraction_can_recheck_only_stable_identical_bytes_after_a_timestamp_change(
        string scenario, string? reason, int expectedReads)
    {
        if (OperatingSystem.IsWindows()) return;
        var reads = 0;
        var timestamps = File.GetLastWriteTimeUtc(_file);
        var capture = ArtifactManifestFiles.CaptureAsync(_root, [_file], path => new FileInfo(path), async (stream, ct) =>
        {
            var hash = await SHA256.HashDataAsync(stream, ct);
            reads++;
            if (reads == 1 && scenario == "content") await File.WriteAllTextAsync(_file, "modified-content", ct);
            if (reads == 1 && scenario is "replaced" or "replaced-preserving-mtime")
            {
                File.Move(_file, _file + ".old");
                await File.WriteAllTextAsync(_file, "verified-content", ct);
            }
            if (reads == 1 && scenario == "size") await File.AppendAllTextAsync(_file, "extra", ct);
            if (scenario == "replaced-preserving-mtime")
                File.SetLastWriteTimeUtc(_file, timestamps);
            else if ((reads == 1 && scenario != "stable") || scenario == "unstable")
                File.SetLastWriteTimeUtc(_file, timestamps.AddMinutes(reads));
            return hash;
        }, verifyTimestampChange: true);
        if (reason == null)
        {
            var result = await capture;
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData("verified-content"u8.ToArray())).ToLowerInvariant(),
                result.Entries.Single().Sha256);
        }
        else
        {
            var failure = await Assert.ThrowsExactlyAsync<IOException>(() => capture);
            StringAssert.EndsWith(failure.Message, "(" + reason + ")");
            Assert.IsFalse(failure.Message.Contains(_root));
        }
        Assert.AreEqual(expectedReads, reads);
    }

    [TestMethod]
    public async Task Actual_size_change_after_open_is_still_rejected()
    {
        await Assert.ThrowsExceptionAsync<IOException>(() => ArtifactManifestFiles.CaptureAsync(_root, [_file],
            path => new FileInfo(path), async (stream, ct) =>
            {
                var hash = await SHA256.HashDataAsync(stream, ct);
                await File.AppendAllTextAsync(_file, "changed", ct);
                return hash;
            }));
    }

    [TestMethod]
    public async Task Path_replaced_with_a_link_during_hashing_is_rejected()
    {
        if (OperatingSystem.IsWindows()) return; // FileShare.Read already denies deletion on Windows.
        var other = Path.Combine(_root, "other.bin");
        File.Copy(_file, other);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtifactManifestFiles.CaptureAsync(_root, [_file],
            path => new FileInfo(path), async (stream, ct) =>
            {
                var hash = await SHA256.HashDataAsync(stream, ct);
                File.Delete(_file);
                File.CreateSymbolicLink(_file, other);
                return hash;
            }));
    }

    [DataTestMethod]
    [DataRow("io")]
    [DataRow("denied")]
    public async Task Unreadable_file_with_existing_root_is_unavailable_not_modified(string failure)
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        Exception error = failure == "io" ? new IOException("Fixture storage read failed.") :
            new UnauthorizedAccessException("Fixture read permission denied.");
        var validation = await ArtifactManifestFiles.ValidateAsync(_root, manifest,
            (root, files, ct) => ArtifactManifestFiles.CaptureAsync(root, files,
                path => new FileInfo(path), (_, _) => ValueTask.FromException<byte[]>(error), ct));

        Assert.AreEqual(ArtifactLocationState.StorageUnavailable, validation.State);
        Assert.IsTrue(File.Exists(_file));
        Assert.AreEqual("verified-content", await File.ReadAllTextAsync(_file));
    }

    [TestMethod]
    public async Task Transient_not_found_while_file_is_still_visible_is_unavailable()
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        var validation = await ArtifactManifestFiles.ValidateAsync(_root, manifest,
            (_, _, _) => Task.FromException<ArtifactManifest>(new FileNotFoundException("Transient lookup failure.", _file)));
        Assert.AreEqual(ArtifactLocationState.StorageUnavailable, validation.State);
    }

    [TestMethod]
    public async Task Confirmed_missing_file_in_readable_parent_remains_missing()
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        File.Delete(_file);
        Assert.AreEqual(ArtifactLocationState.Missing,
            (await ArtifactManifestFiles.ValidateAsync(_root, manifest)).State);
    }

    [TestMethod]
    public async Task Unavailable_root_does_not_confirm_missing_files()
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        Directory.Delete(_root, true);
        Assert.AreEqual(ArtifactLocationState.StorageUnavailable,
            (await ArtifactManifestFiles.ValidateAsync(_root, manifest)).State);
    }

    [TestMethod]
    public async Task Verified_hash_mismatch_remains_modified()
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        await File.WriteAllTextAsync(_file, "modified-content"); // Equal size requires a real hash comparison.
        Assert.AreEqual(ArtifactLocationState.Modified,
            (await ArtifactManifestFiles.ValidateAsync(_root, manifest)).State);
    }

    [TestMethod]
    public async Task Validation_cancellation_is_not_a_storage_state()
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [_file]);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ArtifactManifestFiles.ValidateAsync(_root, manifest, new CancellationToken(true)));
    }
}
