using System.Buffers.Binary;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;
using SharpCompress.Compressors.Deflate;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileProcessingRarVolumeTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "rar-volume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NumericSuffixOnConfirmedStandaloneRarIsNotAMissingVolume(bool rar4)
    {
        var path = Save("archive.111", rar4 ? Rar4(false) : Rar5(false));
        Assert.IsTrue(RarVolumeHeader.ConfirmsSingleArchive(path));
        FileProcessingFiles.ValidateVolumes([path]);
    }

    [TestMethod]
    public void RealRar5MainHeaderWithExtraLocatorConfirmsStandaloneArchive()
    {
        // Main header with a 12-byte locator extra field, from a standalone RAR5 archive.
        var path = Save("archive.111", Convert.FromHexString("526172211a07010081e29b611001050c000b0101a2ebdaca8280808000"));
        Assert.IsTrue(RarVolumeHeader.ConfirmsSingleArchive(path));
        FileProcessingFiles.ValidateVolumes([path]);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RealVolumeHeaderStillRequiresEarlierNumberedVolumes(bool rar4)
    {
        var path = Save("archive.111", rar4 ? Rar4(true) : Rar5(true));
        Assert.IsFalse(RarVolumeHeader.ConfirmsSingleArchive(path));
        Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([path]));
    }

    [TestMethod]
    public void DamagedTruncatedAndEncryptedHeadersDoNotBypassMissingVolumes()
    {
        var corrupt = Rar5(false);
        corrupt[8] ^= 1;
        foreach (var bytes in new[] {corrupt, Rar5(false)[..14], Rar5(false, type: 4), new byte[] {1, 2, 3}})
        {
            var path = Save("archive.111", bytes);
            Assert.IsFalse(RarVolumeHeader.ConfirmsSingleArchive(path));
            Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([path]));
        }
    }

    [TestMethod]
    public void MissingNumericSiblingRemainsProtectedEvenWithStandaloneHeader()
    {
        var first = Save("archive.001", Rar5(false));
        var third = Save("archive.003", new byte[] {1, 2, 3});
        Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([first, third]));
    }

    [TestMethod]
    public void ExplicitPartNamesKeepMissingVolumeProtection()
    {
        var path = Save("archive.part3.rar", Rar5(false));
        Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([path]));
    }

    [TestMethod]
    public void StandaloneHeaderDoesNotBypassUnfinishedDownloadProtection()
    {
        var path = Save("archive.111", Rar5(false));
        Save("archive.111.aria2", []);
        var error = Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([path]));
        StringAssert.Contains(error.Message, "still downloading");
    }

    private string Save(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Rar5(bool volume, byte type = 1)
    {
        byte[] header = [3, type, 0, volume ? (byte)1 : (byte)0];
        var crc = new CRC32();
        crc.SlurpBlock(header, 0, header.Length);
        var checksum = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(checksum, (uint)crc.Crc32Result);
        return [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00, ..checksum, ..header];
    }

    private static byte[] Rar4(bool volume)
    {
        byte[] header = [0x73, volume ? (byte)1 : (byte)0, 0, 13, 0, 0, 0, 0, 0, 0, 0];
        var crc = new CRC32();
        crc.SlurpBlock(header, 0, header.Length);
        var checksum = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(checksum, (ushort)crc.Crc32Result);
        return [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00, ..checksum, ..header];
    }
}
