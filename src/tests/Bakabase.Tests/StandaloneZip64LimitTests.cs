using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed class StandaloneZip64LimitTests
{
    private string _file = "";
    [TestInitialize] public void Initialize() => _file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
    [TestCleanup] public void Cleanup() => File.Delete(_file);

    [DataTestMethod]
    [DataRow(128, true, true)]
    [DataRow(129, true, false)]
    [DataRow(166, true, false)]
    [DataRow(512, true, false)]
    [DataRow(513, false, false)]
    public async Task OnlySelectedStandaloneCanUseTheLargerMemberLimit(int count, bool selected, bool discovered)
    {
        var f = Create(count);
        // A separate reader verifies every generated filename and byte in the positive fixture.
        using (var archive = new ZipArchive(new MemoryStream(f.Bytes), ZipArchiveMode.Read))
        {
            Assert.AreEqual(count, archive.Entries.Count);
            foreach (var entry in archive.Entries)
            {
                using var member = entry.Open();
                Assert.AreEqual((int)'x', member.ReadByte());
                Assert.AreEqual(-1, member.ReadByte());
            }
        }
        Assert.AreEqual(selected, await Selected(f.Bytes));
        var offset = await ArchiveSignature.FindDominantZipAsync(_file, f.Bytes.Length, default);
        Assert.AreEqual(discovered ? (long?)0 : null, offset);
    }

    [DataTestMethod]
    [DataRow("prefix")]
    [DataRow("multipart")]
    [DataRow("entry-disk")]
    [DataRow("count-mirror")]
    [DataRow("directory-shape")]
    [DataRow("crc")]
    [DataRow("name")]
    [DataRow("central-budget")]
    public async Task SelectedInputStillRequiresOneConsistentOffsetZeroDirectory(string variant)
    {
        var f = Create(166, centralComment: variant == "central-budget" ? 400 : 0);
        byte[] bytes = f.Bytes;
        switch (variant)
        {
            case "prefix": bytes = new byte[] {0xff, 0xd8, 0xff, 0xe0}.Concat(bytes).ToArray(); break;
            case "multipart": Put32(bytes, f.End + 72, 2); break;           // ZIP64 locator disk count
            case "entry-disk": Put16(bytes, f.LastCentral + 34, 1); break;
            case "count-mirror": Put64(bytes, f.End + 24, 165); break;
            case "directory-shape": Put64(bytes, f.End + 24, 250); Put64(bytes, f.End + 32, 250); break;
            case "crc": bytes[f.LastCentral + 16] ^= 1; break;
            case "name": bytes[f.LastCentral + 46] ^= 1; break;
            case "central-budget": Assert.IsTrue(f.End - f.Central > 64 * 1024); break;
        }
        Assert.IsFalse(await Selected(bytes), variant);
    }

    [TestMethod]
    public async Task ByteBudgetStillAppliesToLocalHeadersInSelectedArchive()
    {
        var f = Create(166, localExtra: 2048);
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => Selected(f.Bytes));
        StringAssert.Contains(error.Message, "read limit");
    }

    [DataTestMethod]
    [DataRow(240, "multiple plausible")]
    [DataRow(300, "member limit")]
    public async Task SeveralPlausibleEndRecordsShareOneMemberBudget(int count, string reason)
    {
        var f = Create(count);
        int directoryLength = f.End - f.Central;
        using var competing = new MemoryStream();
        competing.Write(f.Bytes);
        competing.Write(f.Bytes, f.Central, directoryLength);
        competing.Write(Footer(count, f.Bytes.Length, directoryLength));
        var data = competing.ToArray();
        Put16(data, f.End + 96, checked((ushort)(directoryLength + 98)));
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => Selected(data));
        StringAssert.Contains(error.Message, reason);
    }

    private async Task<bool> Selected(byte[] bytes)
    {
        await File.WriteAllBytesAsync(_file, bytes);
        return await ArchiveSignature.IsCompleteStandaloneZipAsync(_file, bytes.LongLength, default);
    }

    private sealed record Fixture(byte[] Bytes, int Central, int LastCentral, int End);

    private static Fixture Create(int count, int localExtra = 0, int centralComment = 0)
    {
        const uint crcOfX = 0x8cdc1683;
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.ASCII, true);
        var names = Enumerable.Range(0, count).Select(i => Encoding.ASCII.GetBytes($"e{i:D4}.dat")).ToArray();
        var offsets = new List<uint>();
        foreach (var name in names)
        {
            offsets.Add((uint)bytes.Position);
            writer.Write(0x04034b50u);
            writer.Write((ushort)45); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u);
            writer.Write(crcOfX); writer.Write(1u); writer.Write(1u);
            writer.Write((ushort)name.Length); writer.Write((ushort)localExtra); writer.Write(name);
            if (localExtra > 0)
            {
                writer.Write((ushort)0xcafe); writer.Write((ushort)(localExtra - 4));
                writer.Write(new byte[localExtra - 4]);
            }
            writer.Write((byte)'x');
        }
        int centralStart = (int)bytes.Position, lastCentral = centralStart;
        for (int i = 0; i < count; i++)
        {
            lastCentral = (int)bytes.Position;
            writer.Write(0x02014b50u);
            writer.Write((ushort)45); writer.Write((ushort)45); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u);
            writer.Write(crcOfX); writer.Write(1u); writer.Write(1u);
            writer.Write((ushort)names[i].Length); writer.Write((ushort)0); writer.Write((ushort)centralComment);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u); writer.Write(offsets[i]);
            writer.Write(names[i]); writer.Write(new byte[centralComment]);
        }
        int end = (int)bytes.Position;
        writer.Write(Footer(count, centralStart, end - centralStart));
        return new(bytes.ToArray(), centralStart, lastCentral, end);
    }

    private static byte[] Footer(int count, int centralStart, int centralSize)
    {
        byte[] footer = new byte[98];
        Put32(footer, 0, 0x06064b50); Put64(footer, 4, 44); Put16(footer, 12, 45); Put16(footer, 14, 45);
        Put64(footer, 24, (ulong)count); Put64(footer, 32, (ulong)count);
        Put64(footer, 40, (ulong)centralSize); Put64(footer, 48, (ulong)centralStart);
        Put32(footer, 56, 0x07064b50); Put64(footer, 64, (ulong)(centralStart + centralSize)); Put32(footer, 72, 1);
        Put32(footer, 76, 0x06054b50); Put16(footer, 84, ushort.MaxValue); Put16(footer, 86, ushort.MaxValue);
        Put32(footer, 88, uint.MaxValue); Put32(footer, 92, uint.MaxValue);
        return footer;
    }
    private static void Put16(byte[] data, int pos, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), value);
    private static void Put32(byte[] data, int pos, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pos), value);
    private static void Put64(byte[] data, int pos, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(pos), value);
}
