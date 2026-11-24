using System.Buffers.Binary;
using System.IO.Compression;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class QuickTimeDeliveryAssessmentTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "mov-delivery-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);
    private static byte[] Atom(string type, byte[] payload, bool wide = false)
    {
        var bytes = new byte[payload.Length + (wide ? 16 : 8)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, wide ? 1u : (uint)bytes.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), (ulong)bytes.Length);
        payload.CopyTo(bytes, wide ? 16 : 8); return bytes;
    }
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Movie(bool legacy = false, bool wide = false) => Join(
        legacy ? [] : Atom("ftyp", "qt  \0\0\0\0qt  "u8.ToArray()),
        Atom("mdat", new byte[19], wide),
        Atom("moov", Join(Atom("mvhd", new byte[100]), Atom("trak", Atom("free", [])))));
    private static byte[] M4v(string mediaHandler = "vide", byte[]? payload = null)
    {
        var handler = new byte[24];
        System.Text.Encoding.ASCII.GetBytes(mediaHandler).CopyTo(handler, 8);
        return Join(Atom("ftyp", "M4V \0\0\0\0M4V isomavc1mp42"u8.ToArray()),
            Atom("moov", Join(Atom("mvhd", new byte[100]), Atom("trak",
                Join(Atom("tkhd", new byte[84]), Atom("mdia", Atom("hdlr", handler)))))),
            Atom("mdat", payload ?? new byte[19]));
    }
    private async Task<string> Write(string name, byte[] bytes)
    {
        var path = Path.Combine(root, name); await File.WriteAllBytesAsync(path, bytes); return path;
    }
    private ArtifactRecord Artifact(params string[] paths)
    {
        var entries = paths.Select(p => new ArtifactManifestEntry(Path.GetFileName(p), new FileInfo(p).Length, new string('0', 64))).ToArray();
        return new("artifact", "operation", "processedFiles", root, paths,
            new(entries, ArtifactManifestFiles.Fingerprint(entries)), "fixture", ArtifactLocationState.Available, true, [], null);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task Structured_modern_and_legacy_MOV_are_delivery_files(bool legacy, bool wide)
    {
        var file = await Write("camera.MOV", Movie(legacy, wide));
        Assert.IsTrue(await QuickTimeDeliveryProbe.IsMovieAsync(file, default));
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(file), default, extractionWasReviewed: true);
        Assert.AreEqual(0, result.Files.Count); Assert.IsFalse(result.Automatic);
    }

    [TestMethod]
    public async Task A_large_photo_set_reads_only_its_few_unclassified_MOV_files()
    {
        var files = new List<string>();
        for (var i = 0; i < 40; i++) files.Add(await Write(i + ".jpg", "photo"u8.ToArray()));
        files.Add(await Write("clip.mov", Movie()));
        var artifact = Artifact(files.ToArray());
        // Existing known photo policy is unchanged; no per-photo archive/format probing.
        foreach (var photo in files.Where(f => f.EndsWith(".jpg"))) File.Delete(photo);
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, extractionWasReviewed: true);
        Assert.AreEqual(0, result.Files.Count); Assert.IsFalse(result.Automatic);
    }

    [TestMethod]
    public async Task A_gallery_with_verified_M4V_is_ready_without_opening_known_photo_files()
    {
        var files = new List<string>();
        for (var i = 0; i < 40; i++) files.Add(await Write(i + ".jpg", "photo"u8.ToArray()));
        for (var i = 0; i < 73; i++) files.Add(await Write(i + ".M4V", M4v()));
        var artifact = Artifact(files.ToArray());
        foreach (var photo in files.Take(40)) File.Delete(photo);

        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, extractionWasReviewed: true);

        Assert.AreEqual(0, result.Files.Count);
        Assert.IsFalse(result.Uncertain);
        Assert.IsFalse(result.Automatic);
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsM4vAsync(files[^1], M4v().Length + 1, default));
    }

    [DataTestMethod]
    [DataRow("audio")]
    [DataRow("truncated")]
    [DataRow("appended-zip")]
    [DataRow("embedded-zip")]
    [DataRow("renamed-zip")]
    public async Task A_large_delivery_still_reviews_unverified_or_archive_carrying_M4V(string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var entry = archive.CreateEntry("document.txt").Open())
            entry.Write("archive body"u8);
        var zip = buffer.ToArray();
        var bytes = content switch
        {
            "audio" => M4v("soun"),
            "truncated" => M4v()[..^1],
            "appended-zip" => Join(M4v(), zip),
            "embedded-zip" => M4v(payload: zip),
            "renamed-zip" => zip,
            _ => throw new ArgumentOutOfRangeException(nameof(content))
        };
        var video = await Write("unverified.m4v", bytes);
        var files = new List<string> {video};
        for (var i = 0; i < FileArtifactDeliveryAssessment.MaximumFiles; i++)
            files.Add(await Write(i + ".jpg", "photo"u8.ToArray()));

        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(files.ToArray()), default, extractionWasReviewed: true);

        CollectionAssert.AreEqual(new[] {"unverified.m4v"}, result.Files.ToArray());
        Assert.IsTrue(result.Uncertain);
        Assert.IsFalse(result.Automatic);
    }

    [TestMethod]
    public async Task Movie_suffix_and_FTYP_alone_never_bypass_review()
    {
        foreach (var content in new[] { "not a video"u8.ToArray(), Atom("ftyp", "qt  \0\0\0\0qt  "u8.ToArray()), Movie()[..^3] })
        {
            var path = await Write(Guid.NewGuid() + ".mov", content);
            Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(path, default));
            var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(path), default, extractionWasReviewed: true);
            Assert.IsTrue(result.Uncertain); Assert.IsFalse(result.Automatic);
        }
    }

    [TestMethod]
    public async Task Raw_archive_renamed_MOV_retains_archive_review()
    {
        var file = await Write("disguised.mov", [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 0]);
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(file, default));
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(file), default, extractionWasReviewed: true);
        CollectionAssert.AreEqual(new[] {"disguised.mov"}, result.Files.ToArray());
    }

    [TestMethod]
    public async Task Excess_companion_files_and_mixed_unknown_content_still_wait_without_probing()
    {
        var files = new List<string>();
        for (var i = 0; i <= FileArtifactDeliveryAssessment.MaximumCompanionProbeFiles; i++) files.Add(await Write(i + ".mov", Movie()));
        var artifact = Artifact(files.ToArray()); foreach (var file in files) File.Delete(file);
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, extractionWasReviewed: true)).Uncertain);
        var movie = await Write("clip.mov", Movie()); var key = await Write("keep.key", "application key"u8.ToArray());
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(movie, key), default, extractionWasReviewed: true)).Uncertain);
        Assert.IsTrue(File.Exists(key));
    }

    [DataTestMethod]
    [DataRow(27)]
    [DataRow(FileArtifactDeliveryAssessment.MaximumCompanionProbeFiles)]
    public async Task Companion_probe_budget_is_independent_of_archive_and_display_limits(int count)
    {
        var files = new List<string>();
        for (var i = 0; i < 40; i++) files.Add(await Write(i + ".jpg", "photo"u8.ToArray()));
        for (var i = 0; i < count; i++) files.Add(await Write(i + ".mov", Movie()));
        var artifact = Artifact(files.ToArray());
        foreach (var photo in files.Where(f => f.EndsWith(".jpg"))) File.Delete(photo);
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, extractionWasReviewed: true);
        Assert.AreEqual(0, result.Files.Count);
        Assert.IsFalse(result.Uncertain); Assert.IsFalse(result.Automatic);
        Assert.AreEqual(8, FileArtifactDeliveryAssessment.MaximumFiles, "Archive admission remains separately bounded.");
    }

    [DataTestMethod]
    [DataRow("bad.mov")]
    [DataRow("preserve.key")]
    public async Task A_larger_movie_set_does_not_hide_invalid_or_unknown_companions(string name)
    {
        var files = new List<string>();
        for (var i = 0; i < 27; i++) files.Add(await Write(i + ".mov", Movie()));
        var extra = await Write(name, "unclassified content"u8.ToArray()); files.Add(extra);
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(files.ToArray()), default, extractionWasReviewed: true);
        Assert.IsTrue(result.Uncertain); Assert.IsFalse(result.Automatic);
        Assert.IsTrue(File.Exists(extra));
        Assert.AreEqual(files.Count, Directory.GetFiles(root).Length);
    }

    [TestMethod]
    public async Task Atom_budget_overflow_and_trailing_unknown_bytes_do_not_pass()
    {
        var excess = await Write("too-many.mov", Join(Enumerable.Repeat(Atom("free", []), QuickTimeDeliveryProbe.MaximumAtoms).Append(Movie()).ToArray()));
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(excess, default));
        var trailing = await Write("trailing.mov", Join(Movie(), "PK\x03\x04"u8.ToArray()));
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(trailing, default));
        var overflow = Atom("mdat", [1], wide: true); BinaryPrimitives.WriteUInt64BigEndian(overflow.AsSpan(8), ulong.MaxValue);
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(await Write("overflow.mov", Join(Movie(), overflow)), default));
    }

    [TestMethod]
    public async Task Zero_length_data_atom_can_extend_to_EOF_and_large_payload_is_seeked()
    {
        var file = Path.Combine(root, "large.mov");
        await using (var stream = File.Create(file))
        {
            await stream.WriteAsync(Atom("moov", Join(Atom("mvhd", new byte[100]), Atom("trak", Atom("free", [])))));
            await stream.WriteAsync(new byte[] {0, 0, 0, 0, (byte)'m', (byte)'d', (byte)'a', (byte)'t'});
            stream.SetLength(64 * 1024 * 1024); // Sparse payload, never decoded or scanned.
        }
        Assert.IsTrue(await QuickTimeDeliveryProbe.IsMovieAsync(file, default));
    }

    [TestMethod]
    public async Task Caller_cancellation_is_not_reported_as_a_valid_movie()
    {
        var file = await Write("cancelled.mov", Movie());
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => QuickTimeDeliveryProbe.IsMovieAsync(file, stop.Token));
    }

    [TestMethod]
    public async Task Symbolic_links_and_other_ISO_brands_are_not_MOV_evidence()
    {
        var file = await Write("original.mov", Movie()); var link = Path.Combine(root, "linked.mov");
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(link, file);
            Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(link, default));
        }
        var otherBrand = Movie(); "heic"u8.CopyTo(otherBrand.AsSpan(8)); "heic"u8.CopyTo(otherBrand.AsSpan(16));
        Assert.IsFalse(await QuickTimeDeliveryProbe.IsMovieAsync(await Write("not-video.mov", otherBrand), default));
    }
}
