using System.Buffers.Binary;
using System.IO.Compression;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed class BoundedZipStructureProbeTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "zip-structure-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [DataTestMethod]
    [DataRow(80 * 1024 * 1024)]
    [DataRow(400 * 1024 * 1024)]
    public async Task SparseLargeInputNeedsOnlyTailAndBoundedMetadataReads(int prefix)
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "inert.exe"), prefix,
            trailer: ZipStructureFixture.RarTail);
        Assert.AreEqual((long)prefix, await Probe(file));
        var mode = ArchiveOpenOptions.VerifiedZipPrefix((await Probe(file))!.Value);
        Assert.AreEqual($"-tzip:s{prefix + 1}b", mode.TypeArgument);
        Assert.IsFalse(mode.TypeArgument.Contains(":r"));
    }

    [DataTestMethod]
    [DataRow("disk")]
    [DataRow("centralDisk")]
    [DataRow("diskEntries")]
    [DataRow("zip64Size")]
    [DataRow("zip64Offset")]
    [DataRow("zip64Locator")]
    [DataRow("centralSize")]
    [DataRow("centralEntrySize")]
    [DataRow("entryDisk")]
    [DataRow("localOffset")]
    [DataRow("localCRC")]
    [DataRow("localName")]
    [DataRow("packedSize")]
    [DataRow("truncatedComment")]
    public async Task MalformedOrUnsupportedMetadataNeverGrantsALargerToolScan(string damage)
    {
        var zip = ZipStructureFixture.Zip();
        var end = ZipStructureFixture.Find(zip, 0x06054b50);
        var central = ZipStructureFixture.Find(zip, 0x02014b50);
        void W16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(offset), value);
        void W32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(offset), value);
        switch (damage)
        {
            case "disk": W16(end + 4, 1); break;
            case "centralDisk": W16(end + 6, 1); break;
            case "diskEntries": W16(end + 8, 2); break;
            case "zip64Size": W32(central + 20, uint.MaxValue); break;
            case "zip64Offset": W32(end + 16, uint.MaxValue); break;
            case "zip64Locator": W32(end - 20, 0x07064b50); break;
            case "centralSize": W32(end + 12, uint.MaxValue); break;
            case "centralEntrySize": W16(central + 28, ushort.MaxValue); break;
            case "entryDisk": W16(central + 34, 1); break;
            case "localOffset": W32(central + 42, uint.MaxValue - 1); break;
            case "localCRC": W32(14, 0xdeadbeef); break;
            case "localName": zip[30] ^= 0x10; break;
            case "packedSize": W32(central + 20, uint.MaxValue - 1); break;
            case "truncatedComment": W16(end + 20, 500); break;
        }
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "fixture.mp4"), 40 * 1024 * 1024, zip);
        Assert.IsNull(await Probe(file));
    }

    [DataTestMethod]
    [DataRow(0, 0)]
    [DataRow(40 * 1024 * 1024, 32769)]
    public async Task StartAndTailBoundsRemainFinite(int prefix, int trailer)
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "bounded.mp4"), prefix, trailer: new byte[trailer]);
        Assert.IsNull(await Probe(file));
    }

    [DataTestMethod]
    [DataRow(512 * 1024 * 1024)]
    [DataRow(513 * 1024 * 1024)]
    public async Task ProvenZipBeyondToolLimitIsNotMisreportedAsMissingInstructions(int prefix)
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "large.mp4"), prefix);
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => Probe(file));
        Assert.AreEqual(ArchiveFormatProbeFailureCodes.PrefixLimit, error.Code);
    }

    [TestMethod]
    public async Task TwoValidContainersAreAmbiguousRatherThanSelectingTheLastOne()
    {
        var zip = ZipStructureFixture.Zip();
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "two.mp4"), 40 * 1024 * 1024,
            zip.Concat(zip).ToArray());
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => Probe(file));
        StringAssert.Contains(error.Message, "multiple plausible");
    }

    [TestMethod]
    public async Task StandardZipDoesNotNeedAPrefixHintEvenWithManyEndMarkerBytes()
    {
        var marker = new byte[22]; BinaryPrimitives.WriteUInt32LittleEndian(marker, 0x06054b50);
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "standard.zip"), 0,
            trailer: Enumerable.Range(0, 5).SelectMany(_ => marker).ToArray());
        Assert.IsNull(await Probe(file));
    }

    [DataTestMethod]
    [DataRow("504b0304")]
    [DataRow("504b0506")]
    [DataRow("504b0708")]
    [DataRow("377abcaf271c")]
    [DataRow("526172211a0700")]
    [DataRow("526172211a070100")]
    [DataRow("1f8b08")]
    [DataRow("fd377a585a00")]
    [DataRow("425a6839")]
    public async Task ExistingArchiveHeaderTakesPrecedenceOverAnEmbeddedZip(string signature)
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "outer-archive.bin"), 40 * 1024 * 1024);
        await using (var stream = File.OpenWrite(file)) await stream.WriteAsync(Convert.FromHexString(signature));
        Assert.IsNull(await Probe(file));
    }

    [TestMethod]
    public async Task TarContainerTakesPrecedenceOverAStoredZipMember()
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "outer.tar"), 40 * 1024 * 1024);
        await using (var stream = File.OpenWrite(file))
        {
            stream.Position = 257;
            await stream.WriteAsync("ustar\0"u8.ToArray());
        }
        Assert.IsNull(await Probe(file));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnrequestedZip64ExtraValuesDoNotAuthorizeEitherHeader(bool localHeader)
    {
        var zip = ZipStructureFixture.Zip();
        var central = ZipStructureFixture.Find(zip, 0x02014b50);
        var end = ZipStructureFixture.Find(zip, 0x06054b50);
        var insertion = localHeader ? 30 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(26))
            : central + 46 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(central + 28));
        // Neither size nor offset is a sentinel. An empty block is valid, but an
        // unsolicited 64-bit value must not authorize a different archive structure.
        zip = zip[..insertion].Concat(new byte[] {1, 0, 8, 0, 1, 0, 0, 0, 0, 0, 0, 0}).Concat(zip[insertion..]).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(localHeader ? 28 : central + 30), 12);
        if (localHeader)
            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(end + 12 + 16), (uint)central + 12);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(end + 12 + 12), (uint)(end - central + 12));
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "zip64-extra.mp4"), 40 * 1024 * 1024, zip);
        Assert.IsNull(await Probe(file));
    }

    [TestMethod]
    public async Task DuplicateCentralEntriesCannotAliasTheSameLocalPayload()
    {
        var zip = ZipStructureFixture.Zip();
        var central = ZipStructureFixture.Find(zip, 0x02014b50);
        var end = ZipStructureFixture.Find(zip, 0x06054b50);
        var size = end - central;
        zip = zip[..end].Concat(zip[central..end]).Concat(zip[end..]).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(end + size + 8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(end + size + 10), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(end + size + 12), (uint)(size * 2));
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "overlapping.mp4"), 40 * 1024 * 1024, zip);
        Assert.IsNull(await Probe(file));
    }

    [TestMethod]
    public async Task DataDescriptorMembersUseCentralSizesWithoutReadingTheirPayload()
    {
        var zip = ZipStructureFixture.Zip();
        var central = ZipStructureFixture.Find(zip, 0x02014b50);
        var end = ZipStructureFixture.Find(zip, 0x06054b50);
        var descriptor = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 0x08074b50);
        zip.AsSpan(14, 12).CopyTo(descriptor.AsSpan(4));
        zip = zip[..central].Concat(descriptor).Concat(zip[central..]).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(6), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(central + 16 + 8), 8);
        zip.AsSpan(14, 12).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(end + 16 + 16), (uint)central + 16);
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "descriptor.mp4"), 40 * 1024 * 1024, zip);
        Assert.AreEqual(40L * 1024 * 1024, await Probe(file));
    }

    [TestMethod]
    public async Task RepeatedEndMarkersCannotCreateUnboundedRandomReads()
    {
        var marker = new byte[22]; BinaryPrimitives.WriteUInt32LittleEndian(marker, 0x06054b50);
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "markers.mp4"), 40 * 1024 * 1024,
            Enumerable.Range(0, 5).SelectMany(_ => marker).ToArray());
        await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => Probe(file));
    }

    [TestMethod]
    public async Task ChangedLengthAndCancellationDoNotReturnAnArchiveHint()
    {
        var file = await ZipStructureFixture.Write(Path.Combine(_root, "cancel.mp4"), 40 * 1024 * 1024);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            BoundedZipStructureProbe.FindPrefixAsync(file, new FileInfo(file).Length + 1, default));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            BoundedZipStructureProbe.FindPrefixAsync(file, new FileInfo(file).Length, stop.Token));
    }

    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("duplicate")]
    [DataRow("extra-value")]
    [DataRow("missing-required")]
    public async Task EmptyZip64CentralExtraIsAcceptedOnlyWhenNoValuesAreRequired(string variant)
    {
        var zip = ZipStructureFixture.Zip64WithEmptyCentralExtra(ZipStructureFixture.Zip());
        var central = ZipStructureFixture.Find(zip, 0x02014b50);
        var extra = central + 46 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(central + 28));
        if (variant == "duplicate")
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(extra + 4), 1); // second empty extra is normally an inert unknown field
        if (variant == "extra-value") BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(extra + 2), 4);
        if (variant == "missing-required") BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(central + 20), uint.MaxValue);
        var path = await ZipStructureFixture.Write(Path.Combine(_root, "selected.png"), 4096, zip);
        var result = await BoundedZipStructureProbe.FindStructureAsync(path, new FileInfo(path).Length, default);
        if (variant == "valid") { Assert.AreEqual(4096L, result?.ZipOffset); Assert.IsTrue(result!.Zip64); }
        else Assert.IsNull(result, variant);
    }

    private static Task<long?> Probe(string file) => BoundedZipStructureProbe.FindPrefixAsync(file, new FileInfo(file).Length, default);
}

internal static class ZipStructureFixture
{
    // The marker is deliberately not an instruction to inspect or select the appended format.
    internal static readonly byte[] RarTail = "Rar!\x1a\x07\x01\0inert trailing format"u8.ToArray();
    internal static byte[] Zip()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("payload.txt").Open())) writer.Write("inert payload");
        return bytes.ToArray();
    }
    internal static int Find(byte[] bytes, uint signature)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == signature) return i;
        throw new AssertFailedException("Missing fixture signature.");
    }

    internal static byte[] Zip64WithEmptyCentralExtra(byte[] zip)
    {
        // One-entry synthetic ZIP64, preserving encrypted payload bytes if provided.
        var central = Find(zip, 0x02014b50); var end = Find(zip, 0x06054b50);
        var name = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(26));
        var oldExtra = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(28));
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(central + 20));
        var unpacked = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(central + 24));
        var localExtra = new byte[20]; W16(localExtra, 0, 1); W16(localExtra, 2, 16);
        W64(localExtra, 4, unpacked); W64(localExtra, 12, packed);
        var local = zip[..(30 + name + oldExtra)].Concat(localExtra).Concat(zip[(30 + name + oldExtra)..central]).ToArray();
        W16(local, 28, (ushort)(oldExtra + localExtra.Length)); W32(local, 18, uint.MaxValue); W32(local, 22, uint.MaxValue);
        var record = zip[central..end];
        var recordName = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(28));
        var recordExtra = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(30));
        var emptyExtras = new byte[] {1, 0, 0, 0, 0xff, 0xff, 0, 0};
        record = record[..(46 + recordName)].Concat(emptyExtras).Concat(record[(46 + recordName)..]).ToArray();
        W16(record, 30, (ushort)(recordExtra + emptyExtras.Length));
        var footer = new byte[98]; W32(footer, 0, 0x06064b50); W64(footer, 4, 44);
        W16(footer, 12, 45); W16(footer, 14, 45); W64(footer, 24, 1); W64(footer, 32, 1);
        W64(footer, 40, (ulong)record.Length); W64(footer, 48, (ulong)local.Length);
        W32(footer, 56, 0x07064b50); W64(footer, 64, (ulong)(local.Length + record.Length)); W32(footer, 72, 1);
        W32(footer, 76, 0x06054b50); W16(footer, 84, 1); W16(footer, 86, 1);
        W32(footer, 88, (uint)record.Length); W32(footer, 92, (uint)local.Length);
        return local.Concat(record).Concat(footer).ToArray();
    }

    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void W64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
    internal static async Task<string> Write(string path, int prefix, byte[]? zip = null, byte[]? trailer = null)
    {
        await using var file = File.Create(path);
        if (prefix > 0) { await file.WriteAsync("\0\0\0\u0018ftypisom"u8.ToArray()); file.Position = prefix; }
        await file.WriteAsync(zip ?? Zip());
        if (trailer != null) await file.WriteAsync(trailer);
        return path;
    }
}
