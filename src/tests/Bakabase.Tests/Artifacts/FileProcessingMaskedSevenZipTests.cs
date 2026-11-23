using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public class FileProcessingMaskedSevenZipTests
{
    // An actual uncompressed 7z archive made by 7-Zip, split such that its end header crosses a volume boundary.
    private static readonly byte[] Archive = Convert.FromBase64String(
        "N3q8ryccAARuQRFOFAAAAAAAAABSAAAAAAAAAIpov0JwYXlsb2FkIHZlcmlmaWNhdGlvbgEEBgABCRQABwsBAAEBAAwUAAgKAT5VquAAAAUBERkAYwBvAG4AdABlAG4AdAAuAHQAeAB0AAAAGQIAABQKAQA4Vw33EHndARUGAQAggICBAAA=");
    private string home = null!, root = null!;
    private string[] sources = null!;
    private readonly CancellationToken ct = CancellationToken.None;

    [TestInitialize]
    public void SetUp()
    {
        home = Directory.CreateTempSubdirectory("bakabase-volume-test-").FullName;
        root = Directory.CreateDirectory(Path.Combine(home, "download")).FullName;
        sources = MakeVolumes(root);
    }

    internal static string[] MakeVolumes(string root)
    {
        var head = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var tails = Directory.CreateDirectory(Path.Combine(root, "rest")).FullName;
        string[] sources = [Path.Combine(head, "disguised.7z.mask"), Path.Combine(tails, "bundle.7z.002"), Path.Combine(tails, "bundle.7z.003")];
        for (var i = 0; i < sources.Length; i++) File.WriteAllBytes(sources[i], Archive.Skip(i * 60).Take(60).ToArray());
        return sources;
    }

    [TestCleanup] public void Cleanup() => Directory.Delete(home, true);

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExplicitExtractionOpensRegisteredVolumesTogetherAndPreservesOriginalNames(bool formatInTailNames)
    {
        if (!formatInTailNames)
            for (var i = 1; i < sources.Length; i++)
            {
                var newName = sources[i].Replace("bundle.7z.", "bundle.");
                File.Move(sources[i], newName);
                sources[i] = newName;
            }
        var note = Path.Combine(root, "readme.txt");
        await File.WriteAllTextAsync(note, "keep");
        var tool = new ArchiveTool();
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        var result = await new FileProcessingPlanExecutor(tool).ExecuteAsync(plan, root, [note, .. sources], Path.Combine(home, "work"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.IsTrue(tool.Opened);
        Assert.IsFalse(File.Exists(tool.Entry));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(sources[1])!, "bundle.7z.001")));
        Assert.IsTrue(await SevenZipVolumeSet.CanRecoverAsync(
            $"The archive volume set for {Path.GetFileName(sources[1])} has missing parts.", plan, "open", sources, ct));
        CollectionAssert.AreEqual(Archive, sources.SelectMany(File.ReadAllBytes).ToArray());
        CollectionAssert.AreEquivalent(new[] {"readme.txt", "content.txt"}, result.Files.Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Real_extraction_service_opens_private_numbered_links_as_a_split_7z_archive(bool useRegisteredOriginals)
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var service = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        var work = Path.Combine(home, "work");
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, sources, ct);
        var source = new ArtifactRecord("downloaded-files", "completed-download", "download", root, sources, manifest,
            manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        var result = await new FileProcessingPlanExecutor(service).ExecuteAsync(plan, root, sources, work,
            resolveReadOnlyArchiveInput: useRegisteredOriginals ? _ => Task.FromResult(source) : null);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("payload verification", await File.ReadAllTextAsync(result.Files.Single(), ct));
        var inputs = Directory.EnumerateDirectories(work, "inputs", SearchOption.AllDirectories).Single();
        Assert.AreEqual(useRegisteredOriginals ? 0 : sources.Length, Directory.EnumerateFiles(inputs, "*", SearchOption.AllDirectories).Count());
        Assert.IsTrue(await SevenZipVolumeSet.CanRecoverAsync("Archive structure discovery requires a regular file.", plan, "open", sources, ct));
        CollectionAssert.AreEqual(Archive, sources.SelectMany(File.ReadAllBytes).ToArray());
    }

    [TestMethod]
    [DataRow("startCRC")]
    [DataRow("endCRC")]
    [DataRow("otherFirst")]
    [DataRow("otherFamily")]
    [DataRow("gap")]
    [DataRow("unregisteredFirst")]
    [DataRow("bareNumbersWithForeignData")]
    public async Task IncompleteAmbiguousOrUnverifiedInputsNeverGainAVolumeBinding(string scenario)
    {
        var files = sources.ToList();
        if (scenario is "startCRC" or "endCRC")
        {
            var path = scenario == "startCRC" ? sources[0] : sources[^1];
            var bytes = await File.ReadAllBytesAsync(path); bytes[scenario == "startCRC" ? 8 : bytes.Length - 1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
        }
        if (scenario == "otherFirst") {var p = Path.Combine(root, "another"); File.Copy(sources[0], p); files.Add(p);}
        if (scenario == "otherFamily")
            foreach (var path in sources.Skip(1))
            {
                var newPath = Path.Combine(root, Path.GetFileName(path)); File.Copy(path, newPath); files.Add(newPath);
            }
        if (scenario == "bareNumbersWithForeignData")
        {
            for (var i = 1; i < files.Count; i++)
            {
                var name = files[i].Replace("bundle.7z.", "unrelated.");
                File.Move(files[i], name); files[i] = name;
            }
            var bytes = await File.ReadAllBytesAsync(files[^1]); bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(files[^1], bytes);
        }
        if (scenario == "gap") files.RemoveAt(1);
        if (scenario == "unregisteredFirst") files.RemoveAt(0);
        Assert.AreEqual(0, (await SevenZipVolumeSet.FindAsync(files, ct)).Count);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        Assert.IsFalse(await SevenZipVolumeSet.CanRecoverAsync("Archive structure discovery requires a regular file.", plan, "open", files, ct));
    }


    [TestMethod]
    [DataRow("missing", "MissingVolumes")]
    [DataRow("truncated", "MissingVolumes")]
    [DataRow("duplicate", "ArchiveInputRequired")]
    [DataRow("differentWrapper", "ArchiveInputRequired")]
    [DataRow("startCRC", "ArchiveInputRequired")]
    [DataRow("endCRC", "ArchiveInputRequired")]
    [DataRow("plainConflict", "ArchiveInputRequired")]
    public async Task Wrapped_volumes_with_missing_or_conflicting_evidence_do_not_open_or_copy(string scenario, string reason)
    {
        var registered = new List<string>();
        for (var i = 0; i < sources.Length; i++)
        {
            var ext = scenario == "differentWrapper" && i == 1 ? "bin" : "pdf";
            var path = Path.Combine(root, $"volume.{i+1:D3}~{i+1:x16}.{ext}");
            File.Move(sources[i], path);
            if (i==1 && scenario=="missing") continue;
            registered.Add(path);
        }
        if (scenario == "duplicate" || scenario == "plainConflict")
        {
            var other = Path.Combine(root, scenario == "duplicate" ? "volume.002.pdf" : "volume.002");
            File.Copy(registered[1],other);registered.Add(other);
        }
        if (scenario == "truncated") await File.WriteAllBytesAsync(registered[^1], (await File.ReadAllBytesAsync(registered[^1]))[..^1]);
        if (scenario == "startCRC" || scenario == "endCRC")
        {
            var damaged = scenario == "startCRC" ? registered[0] : registered[^1];
            var bytes = await File.ReadAllBytesAsync(damaged);
            bytes[scenario == "startCRC" ? 8 : bytes.Length - 1] ^= 1;
            await File.WriteAllBytesAsync(damaged, bytes);
        }
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, registered, ct);
        var source = new ArtifactRecord("registered", "download", "download", root, registered, manifest, manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        var work = Path.Combine(home, "work");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        var tool = new ArchiveTool();
        var result = await new FileProcessingPlanExecutor(tool).ExecuteAsync(plan, root, registered, work, resolveReadOnlyArchiveInput: _ => Task.FromResult(source));
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(reason, result.FailureCode);
        Assert.IsFalse(tool.Opened);
        Assert.IsFalse(Directory.Exists(work) && Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Any());
        CollectionAssert.AreEqual(manifest.Entries.ToArray(), (await ArtifactManifestFiles.CaptureAsync(root, registered, ct)).Entries.ToArray());
    }

    private sealed class ArchiveTool : IArchiveExtractionService
    {
        public bool Opened;
        public string? Entry;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string path, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken token)
        {
            Entry = path;
            Assert.AreEqual("archive.7z.001", Path.GetFileName(path));
            return Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        }

        public Task<ArchivePasswordProbe> ProbePasswordAsync(string path, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken token, ArchiveOpenOptions? opening)
        {
            Assert.AreEqual("-t7z.split", opening?.TypeArgument);
            return ProbePasswordAsync(path, passwords, output, token);
        }

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken token)
        {
            Assert.AreEqual("-t7z.split", request.OpenOptions?.TypeArgument);
            CollectionAssert.AreEqual(Archive, request.Files.SelectMany(File.ReadAllBytes).ToArray());
            Assert.AreEqual(3, Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(Entry!)!).Count());
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "content.txt"), "payload verification", token);
            Opened = true;
            return new(true, request.Directory, "");
        }
    }
}
