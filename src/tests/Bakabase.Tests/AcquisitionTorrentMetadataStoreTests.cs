using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Downloads;
using MonoTorrent;
using MonoTorrent.BEncoding;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionTorrentMetadataStoreTests
{
    private string _root = null!;
    private byte[] _metadata = null!;
    private AcquisitionTorrentMetadataStore Store => new(() => Path.Combine(_root, "appdata"));
    private string Storage => Path.Combine(_root, "appdata", "acquisition", "torrent-metadata");

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseMetadata_" + Guid.NewGuid().ToString("N"));
        var files = Path.Combine(_root, "files");
        Directory.CreateDirectory(Path.Combine(files, "nested"));
        await File.WriteAllTextAsync(Path.Combine(files, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(files, "nested", "two.txt"), "two");
        _metadata = (await new TorrentCreator(TorrentType.V1Only) { PieceLength = 16 * 1024 }
            .CreateAsync(new TorrentFileSource(files))).Encode();
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task UploadedMetadata_RoundTripsByContentHash_WithoutTemporaryFilesOrDuplicatedCopies()
    {
        var reference = await Store.SaveAsync(_metadata);
        Assert.IsTrue(AcquisitionTorrentMetadataStore.IsManagedReference(reference));
        Assert.AreEqual(reference, await Store.SaveAsync(_metadata));
        CollectionAssert.AreEqual(_metadata, await Store.ReadAsync(reference));
        Assert.AreEqual(1, Directory.GetFiles(Storage).Length);
        Assert.IsTrue(Directory.GetFiles(Storage).Single().EndsWith(".torrent"));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("not a torrent")]
    public async Task InvalidMetadata_IsRejectedBeforeCreatingStorage(string text)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Store.SaveAsync(Encoding.UTF8.GetBytes(text)));
        Assert.IsFalse(Directory.Exists(Storage));
    }

    [DataTestMethod]
    [DataRow("../escape.txt")]
    [DataRow("/absolute.txt")]
    [DataRow("C:\\escape.txt")]
    [DataRow("nested/../escape.txt")]
    public async Task UnsafeTorrentPaths_CannotBeStored(string path)
    {
        var metadata = BEncodedValue.Decode<BEncodedDictionary>(_metadata);
        var info = (BEncodedDictionary) metadata["info"];
        var files = (BEncodedList) info["files"];
        ((BEncodedDictionary) files[0])["path"] = new BEncodedList { new BEncodedString(path) };
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Store.SaveAsync(metadata.Encode()));
        Assert.IsFalse(Directory.Exists(Storage));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [TestMethod]
    public async Task FourMiBLimit_AcceptsTheExactBoundary_AndRejectsOneExtraByteForUploadsAndStreams()
    {
        var metadata = BEncodedValue.Decode<BEncodedDictionary>(_metadata);
        metadata["comment"] = new BEncodedString("");
        var length = AcquisitionTorrentMetadataStore.MaxMetadataBytes - metadata.Encode().Length;
        byte[] padded;
        do
        {
            metadata["comment"] = new BEncodedString(new string('x', length));
            padded = metadata.Encode();
            length += AcquisitionTorrentMetadataStore.MaxMetadataBytes - padded.Length;
        } while (padded.Length != AcquisitionTorrentMetadataStore.MaxMetadataBytes);
        var reference = await Store.SaveAsync(padded);
        Assert.AreEqual(AcquisitionTorrentMetadataStore.MaxMetadataBytes, (await Store.ReadAsync(reference)).Length);
        await using var exact = new MemoryStream(padded);
        Assert.AreEqual(padded.Length, (await AcquisitionTorrentMetadataStore.ReadBoundedAsync(exact)).Length);
        metadata["comment"] = new BEncodedString(new string('x', length + 1));
        var oversized = metadata.Encode();
        Assert.AreEqual(AcquisitionTorrentMetadataStore.MaxMetadataBytes + 1, oversized.Length);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Store.SaveAsync(oversized));
        await using var stream = new MemoryStream(oversized);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => AcquisitionTorrentMetadataStore.ReadBoundedAsync(stream));
    }

    [DataTestMethod]
    [DataRow("../../private.torrent")]
    [DataRow("file:///private.torrent")]
    [DataRow("bakabase-torrent:../../private")]
    [DataRow("bakabase-torrent:000000000000000000000000000000000000000000000000000000000000000g")]
    public async Task ForgedPathReferences_AreRejectedBeforeFileAccess(string reference)
    {
        Assert.IsFalse(AcquisitionTorrentMetadataStore.IsManagedReference(reference));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Store.ReadAsync(reference));
    }

    [TestMethod]
    public async Task ReplacingStoredBytes_CannotImpersonateTheOriginalReference()
    {
        var reference = await Store.SaveAsync(_metadata);
        await File.WriteAllTextAsync(Directory.GetFiles(Storage).Single(), "tampered bytes");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Store.ReadAsync(reference));
    }
}
