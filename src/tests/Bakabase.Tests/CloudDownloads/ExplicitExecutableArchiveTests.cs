using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ExplicitExecutableArchiveTests
{
    private string _root = null!;
    private string _input = null!;
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "explicit-exe-archive-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public async Task ExplicitExeGlobProbesOnlySelectedManifestAndExtractsRecognizedContainers()
    {
        var a = await Input("one.exe"); var b = await Input("two.exe");
        var outside = await Input("outside.exe"); var note = await Input("note.txt");
        var tool = new Archives {Format = ".zip"};
        var done = await Execute(tool, [a, b, note], "*.exe");
        Assert.IsTrue(done.Completed, done.Message); Assert.AreEqual(2, tool.FormatPaths.Count);
        Assert.AreEqual(2, tool.Extractions); Assert.AreEqual(3, done.Files.Count);
        CollectionAssert.AreEquivalent(new[] {"one.exe", "two.exe"}, tool.FormatPaths.Select(Path.GetFileName).ToArray());
        foreach (var file in new[] {a, b, outside, note}) CollectionAssert.AreEqual("MZinert fixture"u8.ToArray(), await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task ZipSignatureAloneDoesNotAuthorizeAnExeAndNonContainerNeverExtracts()
    {
        var file = await Input("program.exe", "504B030400000000");
        var tool = new Archives();
        var result = await Execute(tool, [file], "*.exe");
        Assert.IsFalse(result.Completed); Assert.IsFalse(result.NeedsPassword);
        Assert.AreEqual(1, tool.FormatPaths.Count); Assert.AreEqual(0, tool.PasswordCalls);
        Assert.AreEqual(0, tool.Extractions);
    }

    [TestMethod]
    public async Task DefaultAndRecursiveDiscoveryNeverProbeExecutableDeliveries()
    {
        var first = await Input("one.exe", "504B030400000000"); var second = await Input("two.exe");
        var tool = new Archives {Format = ".zip"};
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([first, second], false, default, tool)).Count);
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([first, second], true, default, tool,
            exactExtractionSelection: true)).Count);
        Assert.IsFalse((await Execute(tool, [first, second], null)).Completed);
        Assert.AreEqual(0, tool.FormatPaths.Count); Assert.AreEqual(0, tool.Extractions);
    }

    [TestMethod]
    public async Task BroadExecutableSelectionStopsAtBudgetWithoutPartialExtraction()
    {
        var files = new List<string>();
        for (var i = 0; i <= ArchiveFormatProbeLimits.MaximumCandidates; i++) files.Add(await Input($"part-{i}.exe"));
        var tool = new Archives {Format = ".zip"};
        var waiting = await Execute(tool, files, "*.exe");
        Assert.IsFalse(waiting.Completed); StringAssert.Contains(waiting.Message!, "budget");
        Assert.AreEqual(ArchiveFormatProbeLimits.MaximumCandidates, tool.FormatPaths.Count);
        Assert.AreEqual(0, tool.PasswordCalls); Assert.AreEqual(0, tool.Extractions);
    }

    [DataTestMethod]
    [DataRow("none")]
    [DataRow("mz")]
    [DataRow("pe")]
    public async Task InstalledArchiveToolListsTestsAndExtractsAnInertExeNamedZipWithoutExecutingIt(string prefix)
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open());
            writer.Write("inert archive content");
        }
        // The inert stub is only archive input. It has no entry point and is never launched.
        byte[] stub = prefix switch
        {
            "mz" => "MZinert nonexecutable prefix\0"u8.ToArray(),
            "pe" => CreateInertPeHeader(),
            _ => []
        };
        var original = stub.Concat(bytes.ToArray()).ToArray();
        var file = Path.Combine(_input, "archive.exe"); await File.WriteAllBytesAsync(file, original);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        Assert.AreEqual(".zip", await tool.ProbeFormatAsync(file, default));
        var done = await Execute(tool, [file], "*.exe");
        Assert.IsTrue(done.Completed, done.Message);
        Assert.AreEqual("inert archive content", await File.ReadAllTextAsync(done.Files.Single()));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task InstalledToolPeHandlerDoesNotAuthorizeAnOrdinaryExecutableForExtraction()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var bytes = CreateInertPeHeader();
        var file = Path.Combine(_input, "ordinary.exe"); await File.WriteAllBytesAsync(file, bytes);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        Assert.IsNull(await tool.ProbeFormatAsync(file, default));
        var result = await Execute(tool, [file], "*.exe");
        Assert.IsFalse(result.Completed); Assert.IsFalse(result.NeedsPassword);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task InstalledToolRecognizesPePrefixedZipWithManyMembersWithoutExecutingIt()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            for (var i = 0; i < 1500; i++)
            {
                using var writer = new StreamWriter(zip.CreateEntry($"content/entry-{i:D4}.txt").Open());
                writer.Write("inert archive content");
            }
        var stub = CreateInertPeHeader();
        var original = stub.Concat(bytes.ToArray()).ToArray();
        var file = Path.Combine(_input, "many-members.exe");
        await File.WriteAllBytesAsync(file, original);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        Assert.AreEqual(".zip", await tool.ProbeFormatAsync(file, default));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
        CollectionAssert.AreEqual(new[] {file}, Directory.GetFiles(_input));
    }

    // Keep this short-prefix fixture independent of the size of the release or test assemblies.
    internal static byte[] CreateInertPeHeader()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Fixture"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }

    private async Task<string> Input(string name, string? hex = null)
    {
        var file = Path.Combine(_input, name);
        await File.WriteAllBytesAsync(file, hex == null ? "MZinert fixture"u8.ToArray() : Convert.FromHexString(hex));
        return file;
    }
    private Task<FileProcessingPlanResult> Execute(IArchiveExtractionService tool, IReadOnlyList<string> files, string? selector) =>
        new FileProcessingPlanExecutor(tool).ExecuteAsync(new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = selector}]}, _input, files, Path.Combine(_root, "state"));
    private sealed class Archives : IArchiveExtractionService
    {
        public string? Format;
        public List<string> FormatPaths { get; } = [];
        public int PasswordCalls;
        public int Extractions;
        public Task<string?> ProbeFormatAsync(string file, CancellationToken ct)
        { FormatPaths.Add(file); return Task.FromResult(Format); }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct)
        { PasswordCalls++; return Task.FromResult(new ArchivePasswordProbe(true, null, [], "", "")); }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++; Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
