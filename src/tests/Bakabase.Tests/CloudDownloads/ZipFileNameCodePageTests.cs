using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipFileNameCodePageTests
{
    private string _root = null!;
    private string _input = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "zip-name-code-page-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void AbsentCodePagePreservesLegacyIdentityAndExplicitCodePageChangesIt()
    {
        const string legacy = "{\"requirement\":\"required\",\"steps\":[{\"id\":\"open\",\"op\":\"extractArchive\",\"input\":\"download\",\"selector\":null,\"extension\":null,\"password\":null}],\"evidence\":[]}";
        var plan = JsonSerializer.Deserialize<FileProcessingPlan>(legacy, Json)!;
        Assert.AreEqual(legacy, FileProcessingPlanExecutor.IdentityJson(plan));
        Assert.IsFalse(Newtonsoft.Json.JsonConvert.SerializeObject(plan).Contains("ZipFileNameCodePage"));
        var configured = plan with {Steps = [plan.Steps[0] with {ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis}]};
        Assert.AreNotEqual(FileProcessingPlanExecutor.CheckpointIdentity(plan, ["input.zip"]),
            FileProcessingPlanExecutor.CheckpointIdentity(configured, ["input.zip"]));
        Assert.AreEqual(ZipFileNameCodePage.JapaneseShiftJis,
            JsonSerializer.Deserialize<FileProcessingPlan>(JsonSerializer.Serialize(configured, Json), Json)!.Steps[0].ZipFileNameCodePage);
        Assert.AreNotEqual(FileProcessingPlanExecutor.IdentityJson(configured), FileProcessingPlanExecutor.IdentityJson(
            configured with {Steps = [configured.Steps[0] with {ZipFileNameCodePage = ZipFileNameCodePage.SimplifiedChineseGbk}]}));
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(-1)] [DataRow(12345)]
    public void UnknownCodePagesAreRejectedBeforeExecution(int value)
    {
        var codePage = (ZipFileNameCodePage)value;
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Plan(codePage)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ArchiveOpenOptions.WithZipFileNameCodePage(null, codePage));
    }

    [TestMethod]
    public void OnlyExtractionAcceptsCodePageAndOpeningBoundsArePreserved()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(new()
            {Requirement = "required", Steps = [new() {Id = "rename", Op = "renameExtension", Extension = ".zip",
                ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis}]}));
        var prefix = ArchiveOpenOptions.VerifiedZipPrefix(16 * 1024 * 1024);
        var configured = ArchiveOpenOptions.WithZipFileNameCodePage(prefix, ZipFileNameCodePage.JapaneseShiftJis)!;
        Assert.AreEqual(prefix.TypeArgument, configured.TypeArgument);
        Assert.AreEqual(prefix.ExpectedZipOffset, configured.ExpectedZipOffset);
        Assert.IsNull(ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!.TypeArgument);
        Assert.AreSame(prefix, ArchiveOpenOptions.WithZipFileNameCodePage(prefix, null));
    }

    [TestMethod]
    public async Task CodePageDoesNotBecomeAnUnsupportedOptionOnNonZipGroups()
    {
        var input = Path.Combine(_input, "archive.rar"); await File.WriteAllTextAsync(input, "inert fixture");
        var service = new NonZipArchives();
        var result = await new FileProcessingPlanExecutor(service).ExecuteAsync(Plan(ZipFileNameCodePage.JapaneseShiftJis),
            _input, [input], Path.Combine(_root, "state"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.IsNull(service.ProbeOptions); Assert.IsNull(service.ExtractOptions);
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task RealToolCarriesCodePageThroughListingPasswordExtractionAndReuse(bool prefixed)
    {
        var service = new TestableCompressedFileService(Executable());
        var file = Path.Combine(_input, "legacy.zip");
        await File.WriteAllBytesAsync(file, Zip(932, new() {["日本語/添付ソフト.txt"] = "unchanged payload"u8.ToArray()}));
        if (prefixed)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            await using var media = File.Create(file);
            await media.WriteAsync(new byte[] {0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0,
                (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'i', (byte)'s', (byte)'o', (byte)'2'});
            var padding = Enumerable.Repeat((byte)'A', 64 * 1024).ToArray();
            while (media.Position < 16 * 1024 * 1024)
                await media.WriteAsync(padding.AsMemory(0, (int)Math.Min(padding.Length, 16 * 1024 * 1024 - media.Position)));
            await media.WriteAsync(bytes);
        }
        var original = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var tool = new ArchiveExtractionService(service);
        var format = await tool.ProbeAuthorizedFormatAsync(file, default);
        Assert.AreEqual(".zip", format?.Extension);
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(format!.OpenOptions, ZipFileNameCodePage.JapaneseShiftJis);
        if (prefixed)
        {
            Assert.AreEqual("-tzip:s16777217b", opening!.TypeArgument);
            await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => service.GetCompressFileEntries(file, default, opening));
            var rejected = await new FileProcessingPlanExecutor(tool).ExecuteAsync(Plan(ZipFileNameCodePage.JapaneseShiftJis),
                _input, [file], Path.Combine(_root, "state"));
            Assert.AreEqual("ArchiveNameEncodingUnsupported", rejected.FailureCode);
            Assert.IsFalse(rejected.NeedsPassword);
            return;
        }
        var entries = await service.GetCompressFileEntries(file, default, opening);
        Assert.AreEqual(1, entries.Data!.Count);
        Assert.AreEqual("日本語/添付ソフト.txt", entries.Data[0].Path.Replace('\\', '/'));
        var password = await tool.ProbePasswordAsync(file, [null], null, default, opening);
        Assert.IsTrue(password.Succeeded, password.Message);
        var executor = new FileProcessingPlanExecutor(tool);
        var plan = Plan(ZipFileNameCodePage.JapaneseShiftJis);
        var result = await executor.ExecuteAsync(plan, _input, [file], Path.Combine(_root, "state"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("日本語/添付ソフト.txt", Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()).Replace('\\', '/'));
        Assert.AreEqual("unchanged payload", await File.ReadAllTextAsync(result.Files.Single()));
        var before = File.GetLastWriteTimeUtc(result.Files.Single());
        var repeated = await executor.ExecuteAsync(plan, _input, [file], Path.Combine(_root, "state"));
        Assert.AreEqual(result.OutputDirectory, repeated.OutputDirectory);
        Assert.AreEqual(before, File.GetLastWriteTimeUtc(repeated.Files.Single()));
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(file)));
    }

    [TestMethod]
    public async Task RealToolKeepsUtf8DeclarationsAndDynamicNestedSteps()
    {
        var input = Path.Combine(_input, "outer.zip");
        var inner = Zip(65001, new() {["中文/ゲーム.txt"] = "final payload"u8.ToArray()});
        await File.WriteAllBytesAsync(input, Zip(932, new() {["内側.zip"] = inner, ["キー.txt"] = "keep key"u8.ToArray()}));
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "outer", Op = "extractArchive", ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis,
                ExtractionLayout = FileExtractionLayouts.CurrentDirectory},
            new() {Id = "inner", Op = "extractArchive", Input = "outer", Selector = "*.zip",
                ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis, ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]};
        var executor = new FileProcessingPlanExecutor(new ArchiveExtractionService(new TestableCompressedFileService(Executable())));
        var boundary = await executor.ExecuteAsync(plan, _input, [input], Path.Combine(_root, "state"), stopAfterStepId: "outer");
        Assert.IsTrue(boundary.Completed, boundary.Message);
        var result = await executor.ExecuteAsync(plan, _input, [input], Path.Combine(_root, "state"), stopAfterStepId: "inner");
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"キー.txt", "中文/ゲーム.txt"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray());
        Assert.IsTrue(File.Exists(input));
    }

    [DataTestMethod]
    [DataRow(null)] [DataRow(FileExtractionLayouts.CurrentDirectory)]
    public void OperationIntentIncludesExplicitEncodingWithoutChangingLegacyNullShape(string? layout)
    {
        var plan = Plan(null) with {Steps = [Plan(null).Steps[0] with {ExtractionLayout = layout}]};
        var legacy = JsonSerializer.Serialize(plan, Json);
        var explicitNull = legacy.Replace("\"password\":null", "\"password\":null,\"zipFileNameCodePage\":null");
        var config = new FileArtifactProcessingConfig();
        var original = FileArtifactProcessor.Intent("source", legacy, "outer password", config);
        Assert.AreEqual(original, FileArtifactProcessor.Intent("source", explicitNull, "outer password", config));
        var cp932 = plan with {Steps = [plan.Steps[0] with {ZipFileNameCodePage = ZipFileNameCodePage.JapaneseShiftJis}]};
        var cp936 = plan with {Steps = [plan.Steps[0] with {ZipFileNameCodePage = ZipFileNameCodePage.SimplifiedChineseGbk}]};
        var configured = FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(cp932, Json), "outer password", config);
        Assert.AreNotEqual(original, configured);
        Assert.AreNotEqual(configured, FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(cp936, Json), "outer password", config));
    }

    [DataTestMethod]
    [DataRow("ZipCrypto")] [DataRow("AES256")]
    public async Task EncryptedZipHasAnExplicitCapabilityErrorRatherThanPasswordPrompt(string encryption)
    {
        var source = Path.Combine(_root, "日本語.txt");
        await File.WriteAllTextAsync(source, "encrypted fixture");
        var path = Path.Combine(_input, "encrypted.zip");
        var start = new System.Diagnostics.ProcessStartInfo(Executable()) {RedirectStandardOutput = true, RedirectStandardError = true};
        foreach (var arg in new[] {"a", "-tzip", "-mem=" + encryption, "-pfixture-only", path, source}) start.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
            await process.WaitForExitAsync(); Assert.AreEqual(0, process.ExitCode);
        }
        var service = new TestableCompressedFileService(Executable());
        // The normal automatic path still supports the real encrypted archive.
        var automatic = await service.TestCompressedFile(path, "fixture-only", null, null, null, default);
        Assert.AreEqual(0, automatic.ExitCode);
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => service.GetCompressFileEntries(path, default, opening));
        var output = Path.Combine(_root, "rejected");
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => Extract(service, path, output, opening));
        Assert.IsFalse(Directory.Exists(output));
        var result = await new FileProcessingPlanExecutor(new ArchiveExtractionService(service)).ExecuteAsync(
            Plan(ZipFileNameCodePage.JapaneseShiftJis), _input, [path], Path.Combine(_root, "state"));
        Assert.AreEqual("ArchiveNameEncodingUnsupported", result.FailureCode);
        Assert.IsFalse(result.NeedsPassword);
    }

    [TestMethod]
    public async Task MixedLegacyAndUtf8EntriesUseTheirOwnDeclaredEncoding()
    {
        var path = Path.Combine(_input, "mixed.zip");
        await File.WriteAllBytesAsync(path, Zip(932, new() {["ソフト.txt"] = "legacy"u8.ToArray()}));
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, false, Encoding.UTF8))
        using (var output = zip.CreateEntry("中文.txt").Open()) output.Write("utf8"u8);
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        var listed = await service.GetCompressFileEntries(path, default, opening);
        CollectionAssert.AreEquivalent(new[] {"ソフト.txt", "中文.txt"}, listed.Data!.Select(e => e.Path).ToArray());
        var outputDirectory = Path.Combine(_root, "mixed-output");
        await Extract(service, path, outputDirectory, opening);
        Assert.AreEqual("legacy", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "ソフト.txt")));
        Assert.AreEqual("utf8", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "中文.txt")));
    }

    [DataTestMethod]
    [DataRow("traversal")] [DataRow("absolute")] [DataRow("symlink")] [DataRow("collision")] [DataRow("file-directory")] [DataRow("directory-payload")] [DataRow("parent-case")] [DataRow("parent-nfc")]
    public async Task UnsafeDecodedCatalogIsRejectedBeforeAnyOutput(string kind)
    {
        var path = Path.Combine(_input, "unsafe.zip");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.UTF8))
        {
            var first = zip.CreateEntry(kind switch {"traversal" => "../outside.txt", "absolute" => "/outside.txt",
                "directory-payload" => "folder/", "parent-case" => "A/one.txt", "parent-nfc" => "é/one.txt", _ => "file"});
            if (kind == "symlink") first.ExternalAttributes = unchecked((int)0xa1ff0000);
            using (var content = first.Open()) content.Write("payload"u8);
            if (kind is "collision" or "file-directory" or "parent-case" or "parent-nfc")
                using (var second = zip.CreateEntry(kind switch {"collision" => "FILE", "file-directory" => "file/child",
                    "parent-case" => "a/two.txt", _ => "e\u0301/two.txt"}).Open()) second.Write("other"u8);
        }
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        var output = Path.Combine(_root, "unsafe-output");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Extract(service, path, output, opening));
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public async Task PayloadCrcAndCancellationAreChecked()
    {
        var path = Path.Combine(_input, "crc.zip");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.GetEncoding(932)))
        using (var output = zip.CreateEntry("ソフト.txt", CompressionLevel.NoCompression).Open()) output.Write("find-this-payload"u8);
        var bytes = await File.ReadAllBytesAsync(path);
        var offset = bytes.AsSpan().IndexOf("find-this-payload"u8);
        Assert.IsTrue(offset >= 0); bytes[offset] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.TestCompressedFile(path, null, null, null, null, default, opening));
        await File.WriteAllBytesAsync(path, Zip(932, new() {["ソフト.txt"] = new byte[1024 * 1024]}));
        using var cancelled = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExtractWithProgress(path, Path.Combine(_root, "cancelled"),
            "an inherited outer password does not encrypt this ZIP", true, CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput, null, _ => cancelled.Cancel(), null, cancelled.Token, opening));
    }

    [TestMethod]
    public async Task UnicodePathExtraTakesPriorityOverTheLegacyFallback()
    {
        // Python zipfile fixture: ASCII fallback name and a valid Info-ZIP Unicode Path extra field.
        const string fixture = "UEsDBBQAAAAAAAAAIQDgH61uFQAAABUAAAAMAB0AZmFsbGJhY2sudHh0dXAZAAFUVXog5Lit5paHL+OCsuODvOODoC50eHR1bmljb2RlIGV4dHJhIHBheWxvYWRQSwECFAMUAAAAAAAAACEA4B+tbhUAAAAVAAAADAAdAAAAAAAAAAAAgAEAAAAAZmFsbGJhY2sudHh0dXAZAAFUVXog5Lit5paHL+OCsuODvOODoC50eHRQSwUGAAAAAAEAAQBXAAAAXAAAAAAA";
        var path = Path.Combine(_input, "extra.zip");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(fixture));
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        Assert.AreEqual("中文/ゲーム.txt", (await service.GetCompressFileEntries(path, default, opening)).Data!.Single().Path);
        var output = Path.Combine(_root, "unicode-extra");
        await Extract(service, path, output, opening);
        Assert.AreEqual("unicode extra payload", await File.ReadAllTextAsync(Path.Combine(output, "中文", "ゲーム.txt")));
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task LocalHeaderCannotReplaceTheValidatedCentralNameOrEnableEncryption(bool encryptedLocal)
    {
        var path = Path.Combine(_input, "different-headers.zip");
        var bytes = Zip(932, new() {["good.txt"] = "frozen catalog"u8.ToArray()});
        if (encryptedLocal) bytes[6] |= 1; // Test-only corruption of the local general-purpose encryption flag.
        else "evil.txt"u8.CopyTo(bytes.AsSpan(30, 8)); // Equal-length local name; central name remains good.txt.
        await File.WriteAllBytesAsync(path, bytes);
        var service = new TestableCompressedFileService(Executable());
        var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis)!;
        var output = Path.Combine(_root, "header-output");
        if (encryptedLocal)
        {
            await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => Extract(service, path, output, opening));
            Assert.IsFalse(File.Exists(Path.Combine(output, "good.txt")));
        }
        else
        {
            await Extract(service, path, output, opening);
            Assert.AreEqual("frozen catalog", await File.ReadAllTextAsync(Path.Combine(output, "good.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(output, "evil.txt")));
        }
    }

    [TestMethod]
    public async Task CancelledWorkflowExtractionResumesWithoutReusingPartialOutput()
    {
        var path = Path.Combine(_input, "large.zip");
        var payload = Enumerable.Range(0, 1024 * 1024).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, Zip(932, new() {["ソフト.bin"] = payload}));
        var executor = new FileProcessingPlanExecutor(new ArchiveExtractionService(new TestableCompressedFileService(Executable())));
        var plan = Plan(ZipFileNameCodePage.JapaneseShiftJis);
        var state = Path.Combine(_root, "recovery-state");
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(plan, _input, [path], state,
            progress: _ => cancel.Cancel(), ct: cancel.Token));
        var result = await executor.ExecuteAsync(plan, _input, [path], state);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(result.Files.Single()));
        Assert.IsFalse(Directory.EnumerateDirectories(state, "*.pending", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task MultiVolumeRequestIsRejectedBeforeOpeningAnyPart()
    {
        var service = new ArchiveExtractionService(new TestableCompressedFileService(Executable()));
        var output = Path.Combine(_root, "split-output");
        var options = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis);
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => service.ExtractAsync(
            new ArchiveExtractionRequest(["archive.zip.001", "archive.zip.002"], output, OpenOptions: options), null, default));
        Assert.IsFalse(Directory.Exists(output));
    }

    private static Task<CompressedFileService.TestResult> Extract(CompressedFileService service, string path, string output, ArchiveOpenOptions options) =>
        service.ExtractWithProgress(path, output, "unused inherited password", true, CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, options);

    private static FileProcessingPlan Plan(ZipFileNameCodePage? codePage) => new()
        {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", ZipFileNameCodePage = codePage,
            ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]};
    private static string Executable()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive-tool fixture.");
        return path!;
    }
    private static byte[] Zip(int codePage, Dictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true, Encoding.GetEncoding(codePage)))
            foreach (var (name, content) in entries)
                using (var output = archive.CreateEntry(name).Open()) output.Write(content);
        return stream.ToArray();
    }
    private sealed class NonZipArchives : IArchiveExtractionService
    {
        public ArchiveOpenOptions? ProbeOptions { get; private set; }
        public ArchiveOpenOptions? ExtractOptions { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct) => ProbePasswordAsync(file, passwords, output, ct, null);
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct, ArchiveOpenOptions? opening)
        { ProbeOptions = opening; return Task.FromResult(new ArchivePasswordProbe(true, null, [], "", "")); }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            ExtractOptions = request.OpenOptions; Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
