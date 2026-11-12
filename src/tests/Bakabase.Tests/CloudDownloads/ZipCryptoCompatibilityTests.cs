using System.Buffers.Binary;
using System.Security.Cryptography;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipCryptoCompatibilityTests
{
    private string _root = null!;
    private const string Password = "known-密码";
    private static readonly string Payload = string.Concat(Enumerable.Repeat("validated classic zip payload\n", 8));

    // Inert Info-ZIP 3.0 fixtures: zip -X -0/-9 -P <GBK bytes> key.txt.
    // DOS time and CRC have the same password check byte, allowing a descriptor-free
    // variant without implementing cryptography or depending on a ZIP writer in tests.
    private const string Stored = "UEsDBAoACQAAAACvLVEUoZev/AAAAPAAAAAHAAAAa2V5LnR4dJbV3UZZinzr4HsmXDoqA/6G2hA/xVu3gQt15Vm96mrvv6hAyD9ffNxHHu+aqqEabzKYYYMjPU9ME64yTEDdIYUzUo4/ua4F0vplZngiAzOFMai73Njev+Vj+EzP9dEm1Drtn7SqYGpRBGtASDNToDteKLFocG7ekA+2LNScIFH3+0UUM0GWOfhsn+GX9ahdDFZBThFML47cUGh7JUC4BTtPd74v/M+/DGYnTKSGx4RymJl1Y0fxzHYtjMemjVR5a6qMFtVsxBVWPVnPOXfd2+RhGZdjRRunO9wq1mcIi+LOStv7mPnEtTPeKbV79Vz7a8ZtMU8xmG8SAdYvzFBLBwgUoZev/AAAAPAAAABQSwECHgMKAAkAAAAAry1RFKGXr/wAAADwAAAABwAAAAAAAAAAAAAApIEAAAAAa2V5LnR4dFBLBQYAAAAAAQABADUAAAAxAQAAAAA=";
    private const string Deflated = "UEsDBBQACwAIAACvLVEUoZevLwAAAPAAAAAHAAAAa2V5LnR4dCD8njBgFamBpNaoBQ7yRUTaANSTkvqI1fKVQ7UDFyP2Uq561tKMyfkkBW9SbAzhUEsHCBShl68vAAAA8AAAAFBLAQIeAxQACwAIAACvLVEUoZevLwAAAPAAAAAHAAAAAAAAAAEAAACkgQAAAABrZXkudHh0UEsFBgAAAAABAAEANQAAAGQAAAAAAA==";

    [TestInitialize] public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "zipcrypto-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [DataTestMethod]
    [DataRow(0, true)] [DataRow(0, false)] [DataRow(8, true)] [DataRow(8, false)]
    public async Task ExplicitGbkPasswordValidatesAndExtractsClassicZipWithoutChangingSource(int method, bool descriptor)
    {
        var file = await Fixture(method, descriptor);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var service = new Service();
        var wrongEncoding = await service.TestCompressedFile(file, Password, null, null, null, default,
            ArchiveOpenOptions.WithZipPasswordCodePage(null, ZipPasswordCodePage.Utf8));
        Assert.AreEqual(2, wrongEncoding.ExitCode);
        var probe = await new ArchiveExtractionService(service).ProbePasswordAsync(file, [Password], null, default, Options());
        Assert.IsTrue(probe.Succeeded, probe.Message);
        var output = Path.Combine(_root, "result");
        Assert.AreEqual(0, (await Extract(service, file, output)).ExitCode);
        Assert.AreEqual(Payload, await File.ReadAllTextAsync(Path.Combine(output, "key.txt")));
        CollectionAssert.AreEqual(hash, SHA256.HashData(await File.ReadAllBytesAsync(file)));
    }

    [DataTestMethod]
    [DataRow("ciphertext")] [DataRow("size")] [DataRow("crc")] [DataRow("descriptor-crc")]
    [DataRow("local-method")] [DataRow("local-size")] [DataRow("local-flags")]
    [DataRow("strong-encryption")] [DataRow("masked-header")] [DataRow("split-disk")]
    [DataRow("entry-disk")] [DataRow("unsupported-method")] [DataRow("traversal")]
    [DataRow("symlink")]
    public async Task InvalidClassicZipCannotCreateOutputOrChangeSource(string corruption)
    {
        var file = await Fixture(8, true, corruption);
        var original = await File.ReadAllBytesAsync(file);
        var output = Path.Combine(_root, "rejected");
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Extract(new Service(), file, output));
        Assert.IsTrue(error.Code is "ArchiveIntegrityFailure" or "ArchivePasswordEncodingUnsupported" or "ArchiveUnsafeOutput", error.Code);
        Assert.IsFalse(Directory.Exists(output));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
    }

    private async Task<string> Fixture(int method, bool descriptor, string? corruption = null)
    {
        var bytes = Convert.FromBase64String(method == 0 ? Stored : Deflated);
        var central = bytes.AsSpan().IndexOf("PK\u0001\u0002"u8);
        if (!descriptor)
        {
            bytes = [..bytes.AsSpan(0, central - 16), ..bytes.AsSpan(central)];
            central -= 16;
            bytes[6] &= 0xf7; bytes[central + 8] &= 0xf7;
            Write32(bytes, bytes.Length - 22 + 16, (uint)central);
        }
        var end = bytes.Length - 22;
        switch (corruption)
        {
            case "ciphertext": bytes[30 + 7 + 12 + 3] ^= 1; break;
            case "size": Write32(bytes, 22, 3); Write32(bytes, central + 24, 3); Write32(bytes, central - 4, 3); break;
            case "crc": bytes[14] ^= 1; bytes[central + 16] ^= 1; bytes[central - 12] ^= 1; break;
            case "descriptor-crc": bytes[central - 12] ^= 1; break;
            case "local-method": bytes[8] = 0; break;
            case "local-size": Write32(bytes, 22, 1); break;
            case "local-flags": bytes[6] ^= 1; break;
            case "strong-encryption": bytes[6] |= 0x40; bytes[central + 8] |= 0x40; break;
            case "masked-header": bytes[7] |= 0x20; bytes[central + 9] |= 0x20; break;
            case "split-disk": bytes[end + 4] = 1; break;
            case "entry-disk": bytes[central + 34] = 1; break;
            case "unsupported-method": bytes[8] = 12; bytes[central + 10] = 12; break;
            case "traversal": "../evil"u8.CopyTo(bytes.AsSpan(30)); "../evil"u8.CopyTo(bytes.AsSpan(central + 46)); break;
            case "symlink": Write32(bytes, central + 38, 0xa1ff0000); break;
        }
        var file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(file, bytes);
        return file;
    }

    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static ArchiveOpenOptions Options() => ArchiveOpenOptions.WithZipPasswordCodePage(null, ZipPasswordCodePage.SimplifiedChineseGbk)!;
    private static Task<CompressedFileService.TestResult> Extract(CompressedFileService service, string file, string output) =>
        service.ExtractWithProgress(file, output, Password, true, CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, Options());

    private sealed class Service() : CompressedFileService(null!, NullLogger<CompressedFileService>.Instance)
    {
        protected override Task<PythonRuntimeExecution> GetZipCompatibilityExecutionAsync(CancellationToken ct)
        {
            var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_PYZIPPER_PYTHON");
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable)) Assert.Inconclusive("Set BAKABASE_TEST_PYZIPPER_PYTHON to an isolated pyzipper 0.4.0 environment.");
            return Task.FromResult(new PythonRuntimeExecution(new() {Mode = PythonRuntimeMode.External, PythonExecutable = executable}, new("test", "test", executable!, "fixture")));
        }
    }
}
