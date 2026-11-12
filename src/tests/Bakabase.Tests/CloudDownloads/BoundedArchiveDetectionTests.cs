using System.IO.Compression;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using CliWrap;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BoundedArchiveDetectionTests
{
    private string _directory = null!;
    private string _input = null!;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl9sAAAAASUVORK5CYII=");

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bounded-archive-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_directory, "input");
        Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task ThousandOrdinaryImagesNeverTriggerDeepAutomaticOrRecursiveProbes()
    {
        var files = new List<string>();
        for (var i = 0; i < 1000; i++) files.Add(await Input($"image-{i}.png", Png));
        var probe = new CountingProbe();
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync(files, false, default, probe)).Count);
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync(files, true, default, probe,
            exactExtractionSelection: true)).Count);
        var automatic = await new RecursiveArchiveExecutor(probe).ExecuteAsync(_input, files,
            Path.Combine(_directory, "automatic"), new() {MinimumFreeBytes = 0});
        Assert.IsTrue(automatic.Completed, automatic.Message);
        Assert.AreEqual(files.Count, automatic.Files.Count);
        var result = await Execute(probe, files, new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "*.png"}]});
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(0, probe.FormatCalls);
        Assert.AreEqual(0, probe.PasswordCalls);
    }

    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("notRequired")]
    public async Task NoExtractionInstructionNeverInvokesDeepProbe(string requirement)
    {
        var probe = new CountingProbe();
        await Execute(probe, [await Input("opaque.png", Png)], new() {Requirement = requirement});
        Assert.AreEqual(0, probe.FormatCalls);
    }

    [TestMethod]
    public async Task LiteralSmallCandidateIsProbedWithoutAdoptingNearbyImages()
    {
        var small = await Input("selected.png", Png);
        var other = await Input("other.png", Png);
        var probe = new CountingProbe();
        var result = await Execute(probe, [small, other], new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "selected.png"}]});
        Assert.IsFalse(result.Completed); Assert.AreEqual(1, probe.FormatCalls);
        Assert.AreEqual("selected.png", Path.GetFileName(probe.Paths.Single()));
        Assert.AreEqual(0, probe.PasswordCalls);
    }

    [TestMethod]
    public async Task FamiliarExtensionAndHeaderDoNotSpendTheDeepProbeBudget()
    {
        var ordinary = await Input("known.zip", "synthetic"u8.ToArray());
        var disguised = await Input("renamed.data", Convert.FromHexString("504B030400000000"));
        var probe = new CountingProbe();
        Assert.AreEqual(2, (await AuthorizedArchiveDetection.DetectAsync([ordinary, disguised], false,
            default, probe, exactExtractionSelection: true)).Count);
        Assert.AreEqual(0, probe.FormatCalls);
    }

    [TestMethod]
    public async Task FirstDownloadSelectorMismatchWaitsBeforeCreatingStateOrCopyingInputs()
    {
        var file = await Input("package.exe", Png);
        using (var sparse = new FileStream(file, FileMode.Open, FileAccess.Write)) sparse.SetLength(16 * 1024 * 1024);
        var before = new FileInfo(file);
        var probe = new CountingProbe();
        var result = await Execute(probe, [file], new() {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameExtension", Selector = "*.7z*", Extension = ".7z"}]});
        Assert.IsFalse(result.Completed); Assert.AreEqual("rename", result.StepId);
        StringAssert.Contains(result.Message!, "No input files matched");
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "plan")));
        Assert.AreEqual(0, probe.FormatCalls);
        var after = new FileInfo(file);
        Assert.AreEqual(before.Length, after.Length); Assert.AreEqual(before.LastWriteTimeUtc, after.LastWriteTimeUtc);
    }

    [TestMethod]
    public async Task CandidateBudgetExhaustionIsPendingAndNeverExtractsPartialSelection()
    {
        var files = new List<string>();
        for (var i = 0; i <= ArchiveFormatProbeLimits.MaximumCandidates; i++)
            files.Add(await Input($"part-{i}/selected.png", Png));
        var probe = new CountingProbe {Format = ".zip"};
        var result = await Execute(probe, files, new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "selected.png"}]});
        Assert.IsFalse(result.Completed);
        StringAssert.Contains(result.Message!, "budget");
        Assert.AreEqual(ArchiveFormatProbeLimits.MaximumCandidates, probe.FormatCalls);
        Assert.AreEqual(0, probe.PasswordCalls);
    }

    [TestMethod]
    public async Task ProbeLimitIsPendingButCallerCancellationPropagates()
    {
        var file = await Input("selected.png", Png);
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive"}]};
        var limited = new CountingProbe {Limit = true};
        var result = await Execute(limited, [file], plan);
        Assert.IsFalse(result.Completed); StringAssert.Contains(result.Message!, "limit");
        Assert.AreEqual(0, limited.PasswordCalls);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            AuthorizedArchiveDetection.DetectAsync([file], false, stop.Token, limited,
                exactExtractionSelection: true));
    }

    [TestMethod]
    public async Task DeliveryAndSymlinkBoundariesPrecedeDeepProbe()
    {
        var probe = new CountingProbe {Format = ".zip"};
        var delivery = await Input("document.docx", Png);
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([delivery], false, default, probe,
            exactExtractionSelection: true)).Count);
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(_input, "link.png"); File.CreateSymbolicLink(link, delivery);
            await Assert.ThrowsAsync<Exception>(() => AuthorizedArchiveDetection.DetectAsync([link], false,
                default, probe, exactExtractionSelection: true));
        }
        Assert.AreEqual(0, probe.FormatCalls);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(32768)]
    public async Task InstalledToolRecognizesAndExtractsPngPrefixedZipWithoutChangingOriginal(int padding)
    {
        var service = InstalledTool();
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open());
            writer.Write("synthetic archive payload");
        }
        var original = Png.Concat(new byte[padding]).Concat(bytes.ToArray()).ToArray();
        var file = await Input("selected.jpg", original);
        Assert.IsNull(AuthorizedArchiveDetection.Signature(original));
        Assert.AreEqual(".zip", await service.ProbeFormatAsync(file, default));
        var result = await Execute(service, [file], new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive"}]});
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("synthetic archive payload", await File.ReadAllTextAsync(result.Files.Single()));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task InstalledToolRecognizesOrdinaryZipAndRejectsRealPng()
    {
        var service = InstalledTool();
        var png = await Input("normal.png", Png);
        Assert.IsNull(await service.ProbeFormatAsync(png, default));
        var zip = Path.Combine(_input, "normal.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("empty.txt");
        Assert.AreEqual(".zip", await service.ProbeFormatAsync(zip, default));
        CollectionAssert.AreEqual(Png, await File.ReadAllBytesAsync(png));
    }

    [TestMethod]
    public async Task RenamingOnlyFirstVolumeCarriesItsManifestSiblingsAndExtractsARealThreePartArchive()
    {
        var path = InstalledPath();
        var service = new ArchiveExtractionService(new TestableCompressedFileService(path));
        var content = new byte[9000]; new Random(7).NextBytes(content);
        var source = Path.Combine(_directory, "payload.bin"); await File.WriteAllBytesAsync(source, content);
        var log = new StringBuilder();
        await Cli.Wrap(path).WithArguments(["a", "-t7z", "-mx=0", "-v4k", "--",
                Path.Combine(_input, "split.7z"), source], true)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(log)).ExecuteAsync();
        var parts = Directory.GetFiles(_input, "split.7z.*").Order().ToList();
        Assert.AreEqual(3, parts.Count);
        var malformed = parts[0] + "-rename-me"; File.Move(parts[0], malformed); parts[0] = malformed;
        var readme = await Input("readme.txt", "keep this note"u8.ToArray());
        var otherFamily = await Input("split.zip.001", "unselected other family"u8.ToArray());
        var unselected = await Input("outside-manifest.7z.002", "not an input"u8.ToArray());
        var originals = parts.Concat([readme, otherFamily]).ToDictionary(f => f, File.ReadAllBytes);
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameFile", Selector = "split.7z.001-rename-me", TargetName = "split.7z.001"},
             new() {Id = "extract", Op = "extractArchive", Input = "rename", Selector = "split.7z.*"}]};
        var result = await Execute(service, originals.Keys.ToArray(), plan);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(result.Files.Single(f => Path.GetFileName(f) == "payload.bin")));
        CollectionAssert.AreEquivalent(new[] {"payload.bin", "readme.txt", "split.zip.001"}, result.Files.Select(Path.GetFileName).ToArray());
        foreach (var (file, bytes) in originals) CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(file));
        Assert.IsTrue(File.Exists(unselected));
        var stateFile = Directory.GetFiles(Path.Combine(_directory, "plan"), "state.json", SearchOption.AllDirectories).Single();
        var state = System.Text.Json.JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(
            await File.ReadAllTextAsync(stateFile), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var renamed = state.Outputs["rename"];
        CollectionAssert.AreEquivalent(new[] {"split.7z.001", "split.7z.002", "split.7z.003"}, renamed.Select(f => Path.GetFileName(f.Path)).ToArray());
        Assert.AreEqual(1, renamed.Select(f => Path.GetDirectoryName(f.Path)).Distinct().Count());
    }

    private static ArchiveExtractionService InstalledTool()
    {
        return new(new TestableCompressedFileService(InstalledPath()));
    }
    private static string InstalledPath()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH to an installed 7-Zip executable for the synthetic integration test.");
        return path!;
    }
    private async Task<string> Input(string name, byte[] content)
    {
        var path = Path.Combine(_input, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content); return path;
    }
    private Task<FileProcessingPlanResult> Execute(IArchiveExtractionService probe, IReadOnlyList<string> files,
        FileProcessingPlan plan) => new FileProcessingPlanExecutor(probe).ExecuteAsync(plan, _input, files,
        Path.Combine(_directory, "plan"));

    private sealed class CountingProbe : IArchiveExtractionService
    {
        public int FormatCalls;
        public int PasswordCalls;
        public string? Format;
        public bool Limit;
        public List<string> Paths { get; } = [];
        public Task<string?> ProbeFormatAsync(string file, CancellationToken ct)
        {
            FormatCalls++; Paths.Add(file);
            if (Limit) throw new ArchiveFormatProbeIncompleteException("Synthetic output limit reached.");
            return Task.FromResult(Format);
        }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        { PasswordCalls++; throw new AssertFailedException("No password test was expected."); }
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct) => throw new AssertFailedException("No extraction was expected.");
    }
}
