using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ParserArchiveTests
{
    private string _root = null!;
    private string _input = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "parser-archive-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    private static FileProcessingPlan Plan(string? member = null) => new() {Requirement = "required", Steps = [new()
        {Id = "open", Op = "extractArchive", Selector = "outer.bin", ArchiveOpenMode = ArchiveOpenMode.SevenZipParser,
            ArchiveMemberSelector = member, ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]};

    [TestMethod]
    public void NullOpeningRetainsLegacySerializationAndOperationIdentity()
    {
        const string legacy = "{\"requirement\":\"required\",\"steps\":[{\"id\":\"open\",\"op\":\"extractArchive\",\"input\":\"download\",\"selector\":\"outer.bin\",\"extension\":null,\"password\":null}],\"evidence\":[]}";
        var original = JsonSerializer.Deserialize<FileProcessingPlan>(legacy, Json)!;
        Assert.AreEqual(legacy, FileProcessingPlanExecutor.IdentityJson(original));
        var explicitNull = legacy.Replace("\"password\":null", "\"password\":null,\"archiveOpenMode\":null,\"archiveMemberSelector\":null");
        Assert.AreEqual(FileArtifactProcessor.Intent("input", legacy, null, new()),
            FileArtifactProcessor.Intent("input", explicitNull, null, new()));
        var parser = original with {Steps = [original.Steps[0] with {ArchiveOpenMode = ArchiveOpenMode.SevenZipParser}]};
        Assert.AreNotEqual(FileProcessingPlanExecutor.CheckpointIdentity(original, ["outer.bin"]),
            FileProcessingPlanExecutor.CheckpointIdentity(parser, ["outer.bin"]));
        var partial = parser with {Steps = [parser.Steps[0] with {ArchiveMemberSelector = "2.zip"}]};
        Assert.AreNotEqual(FileArtifactProcessor.Intent("input", JsonSerializer.Serialize(parser, Json), null, new()),
            FileArtifactProcessor.Intent("input", JsonSerializer.Serialize(partial, Json), null, new()));
    }

    [DataTestMethod]
    [DataRow("unknown")] [DataRow("rename")] [DataRow("wildcard")] [DataRow("missing-selector")]
    [DataRow("member-path")] [DataRow("member-wildcard")] [DataRow("member-without-mode")] [DataRow("codepage")]
    public void UnsafeOrAmbiguousModeOptionsAreRejected(string kind)
    {
        var step = Plan().Steps[0];
        step = kind switch
        {
            "unknown" => step with {ArchiveOpenMode = (ArchiveOpenMode)1234},
            "rename" => step with {Op = "renameExtension", Extension = ".zip"},
            "wildcard" => step with {Selector = "*.bin"},
            "missing-selector" => step with {Selector = null},
            "member-path" => step with {ArchiveMemberSelector = "../2.zip"},
            "member-wildcard" => step with {ArchiveMemberSelector = "*.zip"},
            "member-without-mode" => step with {ArchiveOpenMode = null, ArchiveMemberSelector = "2.zip"},
            _ => step with {ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis}
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Plan() with {Steps = [step]}));
    }

    [TestMethod]
    public void CatalogRequiresCompleteNonoverlappingSafeSegments()
    {
        const string good = "Type = #\n----------\nPath = 1\nSize = 20\nOffset = 0\nType = \n\nPath = 2.zip\nSize = 80\nOffset = 20\nType = zip\n";
        var catalog = ParserArchiveCatalog.Parse(good, 100, DateTime.UnixEpoch);
        Assert.AreEqual(2, catalog.Members.Count);
        Assert.AreEqual("2.zip", catalog.Selected("2.zip").Single().Name);
        foreach (var bad in new[] {good.Replace("Offset = 20", "Offset = 19"), good.Replace("Size = 80", "Size = 79"),
                     good.Replace("Path = 2.zip", "Path = ../2.zip"), good.Replace("Type = zip", "Type = PE"),
                     good.Replace("Offset = 20", "Offset = 20\nOffset = 20"), good.Replace("Type = #", "Type = zip")})
            Assert.ThrowsExactly<ArchiveFormatProbeIncompleteException>(() => ParserArchiveCatalog.Parse(bad, 100, DateTime.UnixEpoch));
        Assert.ThrowsExactly<ArchiveFormatProbeIncompleteException>(() => catalog.Selected("missing.zip"));
        Assert.ThrowsExactly<ArchiveFormatProbeIncompleteException>(() => ParserArchiveCatalog.Parse(good,
            ParserArchiveCatalog.MaximumSourceBytes + 1, DateTime.UnixEpoch));
    }

    [TestMethod]
    public async Task RealToolPreservesAllUnknownSegmentsAndCopiesTheirExactBytes()
    {
        var (path, prefix, zip, suffix) = await Fixture();
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.Parser();
        Assert.AreEqual("-t#", opening.TypeArgument);
        var listed = (await service.GetCompressFileEntries(path, default, opening)).Data!;
        CollectionAssert.AreEqual(new[] {"1", "2.zip", "3"}, listed.Select(x => x.Path).ToArray());
        var output = Path.Combine(_root, "all");
        await Extract(service, path, output, opening);
        CollectionAssert.AreEqual(prefix, await File.ReadAllBytesAsync(Path.Combine(output, "1")));
        CollectionAssert.AreEqual(zip, await File.ReadAllBytesAsync(Path.Combine(output, "2.zip")));
        CollectionAssert.AreEqual(suffix, await File.ReadAllBytesAsync(Path.Combine(output, "3")));
        CollectionAssert.AreEqual(prefix.Concat(zip).Concat(suffix).ToArray(), await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task ExplicitMemberThenNestedExtractionNeverClaimsToReplaceOriginalContainer()
    {
        var (path, _, _, _) = await Fixture();
        var original = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var companion = Path.Combine(_input, "key.txt"); await File.WriteAllTextAsync(companion, "keep key");
        var plan = Plan("2.zip") with {Steps = [Plan("2.zip").Steps[0], new()
            {Id = "inner", Op = "extractArchive", Input = "open", Selector = "2.zip",
                ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]};
        var executor = new FileProcessingPlanExecutor(new ArchiveExtractionService(new TestableCompressedFileService(Executable())));
        var state = Path.Combine(_root, "state");
        var result = await executor.ExecuteAsync(plan, _input, [path, companion], state);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"key.txt", "original/payload.txt"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray());
        var lineage = result.FileLineage!.Entries.Single(e => e.InputRelativePath == "outer.bin");
        Assert.IsFalse(lineage.Replaced); Assert.AreEqual(0, lineage.OutputRelativePaths.Count);
        var cleanup = await FileProcessingPlanExecutor.GetReclaimableFilesAsync(result.OutputDirectory);
        Assert.IsFalse(cleanup.Any(p => Path.GetFileName(p) == "outer.bin"));
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var time = File.GetLastWriteTimeUtc(result.Files.Single(p => p.EndsWith("payload.txt")));
        var again = await executor.ExecuteAsync(plan, _input, [path, companion], state);
        Assert.IsTrue(again.Completed); Assert.AreEqual(time, File.GetLastWriteTimeUtc(again.Files.Single(p => p.EndsWith("payload.txt"))));
    }

    [TestMethod]
    public async Task MissingMemberIsAPlanProblemWithoutPasswordPromptOrOutput()
    {
        var (path, _, _, _) = await Fixture();
        var executor = new FileProcessingPlanExecutor(new ArchiveExtractionService(new TestableCompressedFileService(Executable())));
        var result = await executor.ExecuteAsync(Plan("missing.zip"), _input, [path], Path.Combine(_root, "missing-state"));
        Assert.IsFalse(result.Completed); Assert.IsFalse(result.NeedsPassword);
        Assert.AreEqual(ArchiveFormatProbeFailureCodes.Incomplete, result.FailureCode);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task SharedExtractionServiceRejectsDeletingAPartiallySelectedContainerBeforeStartingTool()
    {
        var path = Path.Combine(_input, "source.bin");
        await File.WriteAllTextAsync(path, "unselected original bytes");
        var service = new ArchiveExtractionService(new TestableCompressedFileService(Path.Combine(_root, "must-not-run")));
        var output = Path.Combine(_root, "must-not-create");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExtractAsync(new([path], output,
            DeleteAfterDecompression: true, OpenOptions: ArchiveOpenOptions.Parser("2.zip")), null, default));
        Assert.AreEqual("unselected original bytes", await File.ReadAllTextAsync(path));
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public async Task SuccessfulToolExitAndCorrectSizesCannotValidateDifferentSegmentBytes()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix process fixture.");
        var input = Path.Combine(_input, "wrong-bytes.bin"); await File.WriteAllTextAsync(input, new string('a', 100));
        var stub = Path.Combine(_root, "incorrect-parser-tool");
        await File.WriteAllTextAsync(stub, """
            #!/bin/sh
            if [ "$1" = l ]; then
              printf '%s\n' 'Type = #' '----------' 'Path = 1' 'Size = 20' 'Offset = 0' 'Type = ' '' 'Path = 2.zip' 'Size = 80' 'Offset = 20' 'Type = zip' ''
            elif [ "$1" = x ]; then
              for arg in "$@"; do case "$arg" in -o*) target=${arg#-o};; esac; done
              printf '%020d' 0 > "$target/1"
              printf '%080d' 0 > "$target/2.zip"
            fi
            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new TestableCompressedFileService(stub);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Extract(service, input, Path.Combine(_root, "bad-output"), ArchiveOpenOptions.Parser()));
        Assert.AreEqual(new string('a', 100), await File.ReadAllTextAsync(input));
    }

    [DataTestMethod]
    [DataRow("timeout")] [DataRow("output")] [DataRow("cancel")]
    public async Task ParserDiscoveryBoundsStopTheProcess(string kind)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix process fixture.");
        var input = Path.Combine(_input, "stub.bin"); await File.WriteAllTextAsync(input, "inert");
        var stub = Path.Combine(_root, "parser-tool");
        await File.WriteAllTextAsync(stub, "#!/bin/sh\nprintf '%s' $$ > \"$0.pid\"\n" +
            (kind == "output" ? "while :; do printf '%02000d' 0; done\n" : "exec sleep 60\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new TestableCompressedFileService(stub);
        using var stop = new CancellationTokenSource();
        if (kind == "cancel") stop.CancelAfter(300);
        if (kind == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetCompressFileEntries(input, stop.Token, ArchiveOpenOptions.Parser()));
        else
        {
            var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => service.GetCompressFileEntries(input, stop.Token, ArchiveOpenOptions.Parser()));
            Assert.AreEqual(kind == "timeout" ? ArchiveFormatProbeFailureCodes.TimedOut : ArchiveFormatProbeFailureCodes.OutputLimit, error.Code);
        }
        var pid = int.Parse(await File.ReadAllTextAsync(stub + ".pid"));
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); Assert.IsTrue(process.HasExited); }
        catch (ArgumentException) { }
    }

    private async Task<(string Path, byte[] Prefix, byte[] Zip, byte[] Suffix)> Fixture()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("original/payload.txt").Open())) writer.Write("inert payload");
        var prefix = Enumerable.Repeat((byte)'Q', 2048).ToArray(); var suffix = "unrelated trailer remains in working source"u8.ToArray();
        var path = Path.Combine(_input, "outer.bin"); var zip = bytes.ToArray();
        await File.WriteAllBytesAsync(path, prefix.Concat(zip).Concat(suffix).ToArray());
        return (path, prefix, zip, suffix);
    }

    private static Task<CompressedFileService.TestResult> Extract(CompressedFileService service, string path, string destination, ArchiveOpenOptions options) =>
        service.ExtractWithProgress(path, destination, null, true, CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, options);

    private static string Executable()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed tool fixture.");
        return path!;
    }
}
