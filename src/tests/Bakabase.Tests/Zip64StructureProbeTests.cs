using System.Buffers.Binary;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed class Zip64StructureProbeTests
{
    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("locator")]
    [DataRow("disk")]
    [DataRow("count")]
    [DataRow("overflow")]
    [DataRow("local-size")]
    public async Task SingleDiskZip64RequiresMatchingEndDirectoryAndLocalRecords(string damage)
    {
        var root = Path.Combine(Path.GetTempPath(), "zip64-structure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = ZipStructureFixture.Zip();
            var central = ZipStructureFixture.Find(zip, 0x02014b50);
            var end = ZipStructureFixture.Find(zip, 0x06054b50);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(26));
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(18));
            var unpacked = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(22));
            var localExtra = new byte[20];
            W16(localExtra, 0, 1); W16(localExtra, 2, 16);
            W64(localExtra, 4, unpacked); W64(localExtra, 12, packed + (damage == "local-size" ? 1UL : 0UL));
            W32(zip, 18, uint.MaxValue); W32(zip, 22, uint.MaxValue); W16(zip, 28, 20);
            var local = zip[..(30 + nameLength)].Concat(localExtra).Concat(zip[(30 + nameLength)..central]).ToArray();
            var entry = zip[central..end];
            var extra = new byte[28];
            W16(extra, 0, 1); W16(extra, 2, 24);
            W64(extra, 4, unpacked); W64(extra, 12, packed); W64(extra, 20, 0);
            W32(entry, 20, uint.MaxValue); W32(entry, 24, uint.MaxValue); W32(entry, 42, uint.MaxValue);
            W16(entry, 30, 28);
            entry = entry.Concat(extra).ToArray();
            var footer = new byte[98];
            W32(footer, 0, 0x06064b50); W64(footer, 4, 44);
            W16(footer, 12, 45); W16(footer, 14, 45);
            W64(footer, 24, 1); W64(footer, 32, damage == "count" ? 2UL : 1UL);
            W64(footer, 40, (ulong)entry.Length); W64(footer, 48, damage == "overflow" ? ulong.MaxValue : (ulong)local.Length);
            W32(footer, 56, 0x07064b50); W32(footer, 60, damage == "disk" ? 1U : 0U);
            W64(footer, 64, (ulong)(local.Length + entry.Length + (damage == "locator" ? 1 : 0))); W32(footer, 72, 1);
            W32(footer, 76, 0x06054b50); W16(footer, 84, ushort.MaxValue); W16(footer, 86, ushort.MaxValue);
            W32(footer, 88, uint.MaxValue); W32(footer, 92, uint.MaxValue);
            var path = await ZipStructureFixture.Write(Path.Combine(root, "selected.mp4"), 40 * 1024 * 1024,
                local.Concat(entry).Concat(footer).ToArray());
            var offset = await BoundedZipStructureProbe.FindPrefixAsync(path, new FileInfo(path).Length, default);
            if (damage == "valid") Assert.AreEqual(40L * 1024 * 1024, offset);
            else Assert.IsNull(offset, damage);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void W64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
}
