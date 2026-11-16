using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;
using SharpCompress.Compressors.Deflate;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipNameEncodingProbeTests
{
    private string root = null!;
    [TestInitialize] public void Setup()
    {
        root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "zip-name-probe-" + Guid.NewGuid().ToString("N"))).FullName;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [DataTestMethod]
    [DataRow(932, "添付ソフト/画像.txt")]
    [DataRow(936, "中文/文件.txt")]
    [DataRow(950, "繁體/檔案.txt")]
    public async Task UnmarkedLegacyNamesAreRejectedWithoutChoosingACodePage(int codePage, string name)
    {
        var source = await Save(Zip(codePage, name));
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => ZipNameEncodingProbe.EnsureLosslessAsync(source, 0, default));
        Assert.AreEqual(1, Directory.GetFiles(root).Length);
    }

    [DataTestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task ActualUtf8IncludingLiteralReplacementCharacterIsNotLoss(bool declared)
    {
        var bytes = Zip(65001, "合法\ufffd/日本語.txt");
        if (!declared)
        {
            Set16(bytes, 6, (ushort)(U16(bytes, 6) & ~0x800));
            var central = Central(bytes); Set16(bytes, central + 8, (ushort)(U16(bytes, central + 8) & ~0x800));
        }
        await ZipNameEncodingProbe.EnsureLosslessAsync(await Save(bytes), 0, default);
    }

    [DataTestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task UnicodePathExtraRequiresCorrectRawNameCrc(bool validCrc)
    {
        var bytes = UnicodeExtra(Zip(932, "ソフト.txt"), validCrc);
        var source = await Save(bytes);
        if (validCrc) await ZipNameEncodingProbe.EnsureLosslessAsync(source, 0, default);
        else await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => ZipNameEncodingProbe.EnsureLosslessAsync(source, 0, default));
    }

    [TestMethod]
    public async Task ExplicitUtf8FlagDoesNotLetInvalidUtf8HideBehindUnicodeExtra()
    {
        var bytes = UnicodeExtra(Zip(932, "ソフト.txt"), true);
        Set16(bytes, 6, (ushort)(U16(bytes, 6) | 0x800));
        var central = Central(bytes); Set16(bytes, central + 8, (ushort)(U16(bytes, central + 8) | 0x800));
        var source = await Save(bytes);
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => ZipNameEncodingProbe.EnsureLosslessAsync(source, 0, default));
    }

    [TestMethod]
    public async Task VerifiedPrefixIsSupportedWithoutScanningPayload()
    {
        var bytes = new byte[4096].Concat(Zip(932, "ソフト.txt")).ToArray();
        var source = await Save(bytes);
        await ZipNameEncodingProbe.EnsureLosslessAsync(source, 0, default);
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => ZipNameEncodingProbe.EnsureLosslessAsync(source, 4096, default));
    }

    [TestMethod]
    public async Task IncompleteOrSplitCatalogDoesNotInventEncodingEvidence()
    {
        var bytes = Zip(932, "ソフト.txt");
        Set16(bytes, End(bytes) + 4, 1);
        await ZipNameEncodingProbe.EnsureLosslessAsync(await Save(bytes), 0, default);
        bytes = Zip(932, "ソフト.txt"); bytes[Central(bytes)] = 0;
        await ZipNameEncodingProbe.EnsureLosslessAsync(await Save(bytes), 0, default);
    }

    [TestMethod]
    public async Task OversizedCatalogStopsBeforeAllocatingOrReadingIt()
    {
        const int size = 17 * 1024 * 1024;
        var path = Path.Combine(root, "bounded.zip");
        await using (var file = File.Create(path))
        {
            await file.WriteAsync(new byte[] {0x50, 0x4b, 0x03, 0x04});
            file.Position = 4 + size;
            var end = new byte[22]; Set32(end, 0, 0x06054b50);
            Set16(end, 8, 1); Set16(end, 10, 1); Set32(end, 12, size); Set32(end, 16, 4);
            await file.WriteAsync(end);
        }
        await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => ZipNameEncodingProbe.EnsureLosslessAsync(path, 0, default));
    }

    [TestMethod]
    public async Task LegacyNameCheckRunsBeforePasswordTestOrExtractionEvenIfToolWouldClaimSuccess()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This guard covers the Unix archive tool.");
        var sourceBytes = Zip(932, "ソフト.txt"); var source = await Save(sourceBytes);
        var tool = Path.Combine(root, "tool");
        await File.WriteAllTextAsync(tool, "#!/bin/sh\ntouch \"$0.invoked\"\nprintf '%s\\n' 'Everything is Ok'\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new TestableCompressedFileService(tool);
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => service.TestCompressedFile(source, null, null, null, null, default));
        var output = Path.Combine(root, "output");
        await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => service.ExtractWithProgress(source, output, null, true,
            CompressedFileService.OverwriteMode.None, CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default));
        Assert.IsFalse(File.Exists(tool + ".invoked")); Assert.IsFalse(Directory.Exists(output));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(source));
    }

    private async Task<string> Save(byte[] bytes)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(path, bytes); return path;
    }
    private static byte[] Zip(int codePage, string name)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true, Encoding.GetEncoding(codePage)))
            using (var member = zip.CreateEntry(name).Open()) member.Write("small fixture payload"u8);
        return stream.ToArray();
    }
    private static byte[] UnicodeExtra(byte[] bytes, bool validCrc)
    {
        var central = Central(bytes); var end = End(bytes); var nameLength = U16(bytes, central + 28);
        var oldExtra = U16(bytes, central + 30); var at = central + 46 + nameLength + oldExtra;
        var crc = new CRC32(); crc.SlurpBlock(bytes, central + 46, nameLength);
        var name = Encoding.UTF8.GetBytes("正しい名前.txt"); var field = new byte[9 + name.Length];
        Set16(field, 0, 0x7075); Set16(field, 2, (ushort)(5 + name.Length)); field[4] = 1;
        Set32(field, 5, validCrc ? (uint)crc.Crc32Result : (uint)crc.Crc32Result ^ 1); name.CopyTo(field, 9);
        var result = bytes[..at].Concat(field).Concat(bytes[at..]).ToArray();
        Set16(result, central + 30, (ushort)(oldExtra + field.Length));
        Set32(result, end + field.Length + 12, U32(bytes, end + 12) + (uint)field.Length);
        return result;
    }
    private static int Central(byte[] bytes) => (int)U32(bytes, End(bytes) + 16);
    private static int End(byte[] bytes) => bytes.Length - 22;
    private static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static void Set16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    private static void Set32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
}
