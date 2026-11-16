using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using CliWrap;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BoundedPrefixArchiveTests
{
    private string _root = null!;
    private string _input = null!;
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bounded-prefix-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [DataTestMethod]
    [DataRow(".mp4")]
    [DataRow(".zip")]
    [DataRow(".ZIP")]
    public async Task RealToolCarriesBoundedModeThroughListingPasswordAndExplicitExtraction(string suffix)
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var file = await MediaPrefixedZip("download" + suffix, 16 * 1024 * 1024);
        var original = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var compressed = new TestableCompressedFileService(executable!);
        var tool = new ArchiveExtractionService(compressed);
        Assert.IsNull(await tool.ProbeFormatAsync(file, default));
        var format = await tool.ProbeAuthorizedFormatAsync(file, default);
        Assert.AreEqual(".zip", format?.Extension);
        Assert.AreEqual("-tzip:s16777217b", format?.OpenOptions?.TypeArgument);
        var entries = await compressed.GetCompressFileEntries(file, default, format!.OpenOptions);
        Assert.AreEqual(1, entries.Data!.Count);
        var password = await tool.ProbePasswordAsync(file, [null], null, default, format.OpenOptions);
        Assert.IsTrue(password.Succeeded, password.Message);
        var done = await new FileProcessingPlanExecutor(tool).ExecuteAsync(new FileProcessingPlan
        {
            Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = Path.GetFileName(file)}]
        }, _input, [file], Path.Combine(_root, "state"));
        Assert.IsTrue(done.Completed, done.Message);
        Assert.AreEqual("inert payload", await File.ReadAllTextAsync(done.Files.Single()));
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(file)));
    }

    [TestMethod]
    public async Task LargeVerifiedPrefixRequiresCompatibilityDependencyInsteadOfIncreasingTheToolScan()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var file = await ZipStructureFixture.Write(Path.Combine(_input, "oversize.mp4"), 513 * 1024 * 1024);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        var error = await Assert.ThrowsExactlyAsync<DependencyNotInstalledException>(() =>
            tool.ProbeAuthorizedFormatAsync(file, default));
        Assert.AreEqual(PyzipperService.ComponentId, error.ComponentId);
        CollectionAssert.AreEqual(new[] {file}, Directory.GetFiles(_input));
    }

    [DataTestMethod]
    [DataRow(".zip", "-t*:s32m:r")]
    [DataRow(".7z", "-t7z:s32m:r")]
    public async Task PrefixedNonZipArchiveKeepsCorrectTypedOpeningAndRecoversFromAMisleadingKnownSuffix(
        string suffix, string expectedOpening)
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var payload = Path.Combine(_root, "payload.txt");
        await File.WriteAllTextAsync(payload, "inert payload");
        var archive = Path.Combine(_root, "fixture.7z");
        await Cli.Wrap(executable!).WithArguments(["a", "-t7z", archive, payload], true).ExecuteAsync();
        var file = await MediaPrefixedArchive("download" + suffix, 16 * 1024 * 1024, await File.ReadAllBytesAsync(archive));
        var original = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var compressed = new TestableCompressedFileService(executable!);
        var tool = new ArchiveExtractionService(compressed);
        Assert.IsNull(await tool.ProbeFormatAsync(file, default));
        var format = await tool.ProbeAuthorizedFormatAsync(file, default);
        Assert.AreEqual(".7z", format?.Extension);
        Assert.AreEqual(expectedOpening, format?.OpenOptions?.TypeArgument);
        var entries = await compressed.GetCompressFileEntries(file, default, format!.OpenOptions);
        Assert.AreEqual(1, entries.Data!.Count);
        var password = await tool.ProbePasswordAsync(file, [null], null, default, format!.OpenOptions);
        Assert.IsTrue(password.Succeeded, password.Message);
        var done = await new FileProcessingPlanExecutor(tool).ExecuteAsync(new FileProcessingPlan
        {
            Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = Path.GetFileName(file)}]
        }, _input, [file], Path.Combine(_root, "state"));
        Assert.IsTrue(done.Completed, done.Message);
        Assert.AreEqual("inert payload", await File.ReadAllTextAsync(done.Files.Single()));
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(file)));
    }

    [DataTestMethod]
    [DataRow(".mp4")]
    [DataRow(".zip")]
    public async Task RealToolOpensVerifiedLargePrefixZipWithoutSelectingTheTrailingFormat(string suffix)
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var file = await MediaPrefixedZip("large" + suffix, 40 * 1024 * 1024);
        await using (var tail = new FileStream(file, FileMode.Append)) await tail.WriteAsync(ZipStructureFixture.RarTail);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        Assert.IsNull(await tool.ProbeFormatAsync(file, default));
        var format = await tool.ProbeAuthorizedFormatAsync(file, default);
        Assert.AreEqual(".zip", format?.Extension);
        Assert.AreEqual("-tzip:s41943041b", format?.OpenOptions?.TypeArgument);
        var password = await tool.ProbePasswordAsync(file, [null], null, default, format!.OpenOptions);
        Assert.IsTrue(password.Succeeded, password.Message);
        var result = await new FileProcessingPlanExecutor(tool).ExecuteAsync(new FileProcessingPlan
        {
            Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Selector = Path.GetFileName(file)}]
        }, _input, [file], Path.Combine(_root, "state"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("inert payload", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    public async Task RealToolConfirmsSparsePrefixBeyond128MiBWithinTheOriginalBudget()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var file = await ZipStructureFixture.Write(Path.Combine(_input, "large-media.mp4"),
            400 * 1024 * 1024, trailer: ZipStructureFixture.RarTail);
        var tool = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        var format = await tool.ProbeAuthorizedFormatAsync(file, default);
        Assert.AreEqual(".zip", format?.Extension);
        Assert.AreEqual("-tzip:s419430401b", format?.OpenOptions?.TypeArgument);
    }

    [TestMethod]
    public async Task RealToolKeepsJpegWrappedRar5ReadableAfterRenamingToRar()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        // A generated gray pixel with a legal JPEG comment, then one stored RAR5 member.
        var jpeg = Convert.FromBase64String("/9j/wAALCAABAAEBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAA" +
            "QRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZn" +
            "aGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6e" +
            "rx8vP09fb3+Pn6/9sAQwACAgICAgIDAgIDBQMDAwUGBQUFBQYIBgYGBgYICggICAgICAoKCgoKCgoKDAwMDAwMDg4ODg4PDw8P" +
            "Dw8PDw8P/90ABAAB/9oACAEBAAA/ACv/2Q==");
        const int offset = 54198;
        var bytes = new byte[offset];
        bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff; bytes[3] = 0xfe;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), (ushort)(offset - jpeg.Length - 2));
        jpeg.AsSpan(2).CopyTo(bytes.AsSpan(offset - jpeg.Length + 2));
        var rar = Convert.FromBase64String("UmFyIRoHAQDFGjMyAwEAAIU9g6AYAgIQBBAAQWRaIgAAC3BheWxvYWQudHh0aGVsbG8gZnJvbSB0ZXN0ChmyOjUDBQAA");
        var original = Path.Combine(_input, "download.jpg");
        await File.WriteAllBytesAsync(original, [..bytes, ..rar]);
        var compressed = new TestableCompressedFileService(executable!);
        var tool = new ArchiveExtractionService(compressed);
        Assert.AreEqual(".rar", await tool.ProbeFormatAsync(original, default));
        var renamed = Path.ChangeExtension(original, ".rar");
        File.Move(original, renamed);
        var digest = SHA256.HashData(await File.ReadAllBytesAsync(renamed));
        var guided = await tool.ProbeAuthorizedFormatAsync(renamed, default);
        Assert.AreEqual(".rar", guided?.Extension);
        Assert.AreEqual("-trar5:s54199b", guided!.OpenOptions?.TypeArgument);
        var entries = await compressed.GetCompressFileEntries(renamed, default);
        Assert.AreEqual("payload.txt", entries.Data!.Single().Path);
        var password = await tool.ProbePasswordAsync(renamed, [null], null, default);
        Assert.IsTrue(password.Succeeded, password.Message);
        var result = await new FileProcessingPlanExecutor(tool).ExecuteAsync(new FileProcessingPlan
        {
            Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Selector = "download.rar"}]
        }, _input, [renamed], Path.Combine(_root, "state"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("hello from test\n", await File.ReadAllTextAsync(result.Files.Single()));
        CollectionAssert.AreEqual(digest, SHA256.HashData(await File.ReadAllBytesAsync(renamed)));
    }

    private async Task<string> MediaPrefixedZip(string name, int offset)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open());
            writer.Write("inert payload");
        }
        return await MediaPrefixedArchive(name, offset, bytes.ToArray());
    }

    private async Task<string> MediaPrefixedArchive(string name, int offset, byte[] archive)
    {
        var file = Path.Combine(_input, name);
        await using var stream = File.Create(file);
        // A sparse all-zero fake media body also happens to be accepted as COFF by 7-Zip.
        // Use actual ftyp/mdat framing and non-zero inert media bytes instead.
        await stream.WriteAsync(new byte[] {0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0,
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'i', (byte)'s', (byte)'o', (byte)'2'});
        var box = new byte[8]; BinaryPrimitives.WriteInt32BigEndian(box, offset - 24);
        "mdat"u8.CopyTo(box.AsSpan(4)); await stream.WriteAsync(box);
        var padding = new byte[64 * 1024]; Array.Fill(padding, (byte)'A');
        while (stream.Position < offset)
            await stream.WriteAsync(padding.AsMemory(0, (int)Math.Min(padding.Length, offset - stream.Position)));
        await stream.WriteAsync(archive);
        return file;
    }
}
