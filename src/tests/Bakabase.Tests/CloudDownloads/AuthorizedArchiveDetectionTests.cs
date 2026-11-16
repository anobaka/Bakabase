using System.IO.Compression;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class AuthorizedArchiveDetectionTests
{
    private string _directory = null!;
    private string _input = null!;
    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "authorized-archive-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_directory, "input"); Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [DataTestMethod]
    [DataRow("377ABCAF271C0004", ".7z")]
    [DataRow("504B030400000000", ".zip")]
    [DataRow("504B050600000000", ".zip")]
    [DataRow("526172211A0700", ".rar")]
    [DataRow("526172211A070100", ".rar")]
    [DataRow("504B03", null)]
    [DataRow("377ABCAF27", null)]
    [DataRow("0000377ABCAF271C", null)]
    [DataRow("66696C652E377A00", null)]
    public void OnlyCompleteSupportedMarkersAtOffsetZeroBecomeCandidates(string bytes, string? format) =>
        Assert.AreEqual(format, AuthorizedArchiveDetection.Signature(Convert.FromHexString(bytes)));

    [DataTestMethod]
    [DataRow("377ABCAF271C0004", ".7z")]
    [DataRow("526172211A0700", ".rar")]
    [DataRow("526172211A070100", ".rar")]
    public async Task HeaderFallbackKeepsTheExactOriginalPathAndReportsTheDetectedContainer(string bytes, string format)
    {
        var file = await Bytes("opaque.payload", bytes);
        foreach (var recursive in new[] {false, true})
        {
            var group = (await AuthorizedArchiveDetection.DetectAsync([file], recursive, default)).Single();
            Assert.AreEqual(format, group.Extension);
            CollectionAssert.AreEqual(new[] {file}, group.Files);
            CollectionAssert.AreEqual(new[] {(long)bytes.Length / 2}, group.FileSizes);
        }
        CollectionAssert.AreEqual(new[] {file}, Directory.GetFiles(_input));
    }

    [TestMethod]
    public async Task ExactMultipartGroupingPrecedesSignatureFallbackAndNeverAdoptsNearbyFiles()
    {
        var first = await Bytes("split.7z.001", "377ABCAF271C0004");
        var second = await Bytes("split.7z.002", "504B030400000000");
        var nearby = await Bytes("split.7z.003", "526172211A070100");
        var unselected = await Bytes("unselected.blob", "504B030400000000");
        var groups = await AuthorizedArchiveDetection.DetectAsync([second, first], recursive: true, default);
        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEqual(new[] {first, second}, groups[0].Files.ToArray());
        Assert.IsFalse(groups.SelectMany(g => g.Files).Contains(nearby));
        Assert.IsFalse(groups.SelectMany(g => g.Files).Contains(unselected));
        Assert.IsTrue(File.Exists(nearby)); Assert.IsTrue(File.Exists(unselected));
    }

    [TestMethod]
    public async Task Sfx_probe_keeps_every_authorized_volume_and_never_probes_later_parts_independently()
    {
        var first = await Bytes("bundle.part1.exe", "4D5A000000000000");
        var second = await Bytes("bundle.part2.rar", "504B030400000000");
        var third = await Bytes("unrelated.part3.rar", "526172211A070100");
        var probe = new SfxProbe(".rar");
        var groups = await AuthorizedArchiveDetection.DetectAsync([second, first], false, default, probe,
            exactExtractionSelection: true);
        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEqual(new[] {first, second}, groups[0].Files.ToArray());
        CollectionAssert.AreEqual(new[] {first}, probe.Calls.ToArray());
        Assert.AreEqual("bundle", groups[0].KeyName);
        Assert.IsFalse(groups[0].Files.Contains(third));
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([first, second], true, default, probe)).Count);
        Assert.AreEqual(1, probe.Calls.Count, "Recursive discovery must not promote SFX candidates or probe their later volumes.");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow(".zip")]
    public async Task An_unconfirmed_Sfx_family_is_not_partially_recognized_as_a_later_Rar(string? format)
    {
        var first = await Bytes("bundle.part1.exe", "4D5A000000000000");
        var second = await Bytes("bundle.part2.rar", "504B030400000000");
        var probe = new SfxProbe(format);
        await Assert.ThrowsExceptionAsync<ArchiveFormatProbeIncompleteException>(() =>
            AuthorizedArchiveDetection.DetectAsync([first, second], false, default, probe, exactExtractionSelection: true));
        CollectionAssert.AreEqual(new[] {first}, probe.Calls.ToArray());
    }

    [DataTestMethod]
    [DataRow("book.docx")]
    [DataRow("sheet.xlsx")]
    [DataRow("book.epub")]
    [DataRow("app.apk")]
    [DataRow("program.exe")]
    [DataRow("application.jar")]
    [DataRow("comic.cbz")]
    [DataRow("book.part1.docx")]
    [DataRow("comic.part1.cbz")]
    public async Task DeliveryFormatsRemainFilesEvenWithArchiveSignatures(string name)
    {
        var file = await Bytes(name, "504B030400000000");
        foreach (var recursive in new[] {false, true})
            Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([file], recursive, default)).Count);
        Assert.AreEqual("504B030400000000", Convert.ToHexString(await File.ReadAllBytesAsync(file)));
    }

    [DataTestMethod]
    [DataRow("archive.s")]
    [DataRow("archive.blob")]
    [DataRow("archive.7z删除")]
    public async Task ExplicitExtractionUsesRealZipContentWithoutRenamingOrChangingOriginal(string name)
    {
        var file = Path.Combine(_input, name); var original = ZipPayload();
        await File.WriteAllBytesAsync(file, original);
        var archives = new ZipArchives(); var executor = new FileProcessingPlanExecutor(archives);
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "open", Op = "extractArchive", Password = "fixture"}]};
        var result = await executor.ExecuteAsync(plan, _input, [file], Path.Combine(_directory, "plan"));
        Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual(1, archives.Extractions);
        Assert.AreEqual(name, Path.GetFileName(archives.Probed.Single()));
        Assert.AreEqual("fixture", archives.Passwords.Single());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
        CollectionAssert.AreEqual(new[] {file}, Directory.GetFiles(_input));
    }

    [TestMethod]
    public async Task RecursiveExtractionProcessesNewDisguisedOutputsButLeavesRandomDeliveriesAndUnselectedArchives()
    {
        var inner = ZipPayload();
        var file = Path.Combine(_input, "outer.opaque");
        var outer = ZipPayload(new Dictionary<string, byte[]> {
            ["inner.unknown"] = inner, ["document.docx"] = inner, ["readme.bin"] = "random data"u8.ToArray() });
        await File.WriteAllBytesAsync(file, outer);
        var unrelated = Path.Combine(_input, "unselected.zip"); await File.WriteAllBytesAsync(unrelated, inner);
        var archives = new ZipArchives();
        var result = await new RecursiveArchiveExecutor(archives).ExecuteAsync(_input, [file],
            Path.Combine(_directory, "recursive"), new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual(2, archives.Extractions);
        Assert.AreEqual(2, result.Lineage!.Count);
        Assert.IsTrue(result.Files.Any(f => Path.GetFileName(f) == "payload.txt"));
        Assert.IsTrue(result.Files.Any(f => Path.GetFileName(f) == "document.docx"));
        Assert.IsTrue(result.Files.Any(f => Path.GetFileName(f) == "readme.bin"));
        Assert.IsFalse(archives.Probed.Any(f => Path.GetFileName(f) is "document.docx" or "readme.bin" or "unselected.zip"));
        CollectionAssert.AreEqual(outer, await File.ReadAllBytesAsync(file));
        CollectionAssert.AreEqual(inner, await File.ReadAllBytesAsync(unrelated));
    }

    [TestMethod]
    public async Task RandomBytesAreNotTreatedAsArchivesAndHeaderAloneDoesNotClaimSuccess()
    {
        var random = await Bytes("random.blob", "0001020304050607");
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([random], true, default)).Count);
        var archives = new ZipArchives(); var executor = new FileProcessingPlanExecutor(archives);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        var noCandidate = await executor.ExecuteAsync(plan, _input, [random], Path.Combine(_directory, "random-state"));
        Assert.IsFalse(noCandidate.Completed); Assert.AreEqual(0, archives.Probed.Count);
        var truncated = await Bytes("truncated.blob", "504B030400000000");
        var invalid = await executor.ExecuteAsync(plan, _input, [truncated], Path.Combine(_directory, "truncated-state"));
        Assert.IsFalse(invalid.Completed, "The existing archive service must still validate the candidate.");
        Assert.AreEqual(1, archives.Probed.Count); Assert.AreEqual(0, archives.Extractions);
    }

    [TestMethod]
    public async Task RecursiveCandidateSubsetDoesNotAuthorizeOtherSignatureMatchesInTheSameInputManifest()
    {
        var first = Path.Combine(_input, "selected.blob"); var second = Path.Combine(_input, "other.blob");
        await File.WriteAllBytesAsync(first, ZipPayload()); await File.WriteAllBytesAsync(second, ZipPayload());
        var archives = new ZipArchives();
        var result = await new RecursiveArchiveExecutor(archives).ExecuteAsync(_input, [first, second],
            Path.Combine(_directory, "subset"), new() {MinimumFreeBytes = 0}, archiveCandidates: [first]);
        Assert.IsTrue(result.Completed); Assert.AreEqual(1, archives.Extractions);
        Assert.IsTrue(result.Files.Any(f => Path.GetFileName(f) == "other.blob"));
        CollectionAssert.AreEqual(new[] {first}, result.SourceArchiveFiles!.ToArray());
    }

    private async Task<string> Bytes(string name, string hex)
    {
        var path = Path.Combine(_input, name); await File.WriteAllBytesAsync(path, Convert.FromHexString(hex)); return path;
    }
    private static byte[] ZipPayload(Dictionary<string, byte[]>? files = null)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (name, content) in files ?? new() { ["payload.txt"] = "payload"u8.ToArray() })
            {
                using var output = archive.CreateEntry(name).Open(); output.Write(content);
            }
        return memory.ToArray();
    }

    private sealed class ZipArchives : IArchiveExtractionService
    {
        public List<string> Probed { get; } = [];
        public List<string?> Passwords { get; } = [];
        public int Extractions { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            Probed.Add(entry); Passwords.Add(candidates.FirstOrDefault());
            try { using var archive = ZipFile.OpenRead(entry); return Task.FromResult(new ArchivePasswordProbe(true, candidates.FirstOrDefault(), [], "", "")); }
            catch (InvalidDataException) { return Task.FromResult(new ArchivePasswordProbe(false, null, [], "", "Invalid archive")); }
        }
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++; ZipFile.ExtractToDirectory(request.Files.Single(), request.Directory);
            return Task.FromResult(new ArchiveExtractionResult(true, request.Directory, ""));
        }
    }

    private sealed class SfxProbe(string? format) : IArchiveExtractionService
    {
        public List<string> Calls { get; } = [];
        public Task<string?> ProbeFormatAsync(string path, CancellationToken ct)
        { Calls.Add(path); return Task.FromResult(format); }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => throw new AssertFailedException("Detection must not test a password.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct) => throw new AssertFailedException("Detection must not extract files.");
    }
}
