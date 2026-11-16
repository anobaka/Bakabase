using System.Buffers.Binary;
using System.IO.Compression;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SplitZip_naming_evidence_orders_disks_and_binds_the_exact_rename_only(bool zip64)
    {
        using var fixture = new MultipartEvidenceFixture();
        var parts = SingleFileZipDisks(zip64);
        var input = fixture.Input(["item/set.z删ip", "item/set.z删02", "item/set.z删01"], [parts[2], parts[1], parts[0]]);
        var evidence = await Inspect(input);
        Assert.IsNotNull(evidence);
        Assert.AreEqual("zip", evidence.Format);
        Assert.AreEqual(parts.Sum(p => (long)p.Length), evidence.DeclaredArchiveBytes);
        CollectionAssert.AreEqual(new[] {new MultipartRename(3, "set.z01"), new(2, "set.z02"), new(1, "set.zip")},
            evidence.Renames.ToArray());
        var paths = input.Manifest.Entries.Select(e => e.RelativePath).ToArray();
        var extract = Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""");
        Assert.IsTrue(AdaptiveFileProcessingAssessmentService.RequireMultipartNaming(extract, evidence, paths).NeedsHumanReview);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "item", "set.zip")));
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("mixed-directory")]
    [DataRow("mixed-basename")]
    [DataRow("mixed-mask")]
    [DataRow("other-container")]
    [DataRow("incorrect-disk-count")]
    [DataRow("wrong-filename")]
    [DataRow("wrong-size")]
    [DataRow("wrong-zip64-offset")]
    [DataRow("canonical")]
    public async Task SplitZip_ambiguous_names_or_conflicting_structure_supply_no_naming_advice(string change)
    {
        using var fixture = new MultipartEvidenceFixture();
        var names = new[] {"data.zmask01", "data.zmask02", "data.zmaskip"};
        var parts = SingleFileZipDisks(true);
        var central = Array.IndexOf(parts[2], (byte)'P', 0);
        switch (change)
        {
            case "missing": names[1] = "data.zmask03"; break;
            case "mixed-directory": names[1] = "sibling/" + names[1]; break;
            case "mixed-basename": names[0] = "set.zmask01"; break;
            case "mixed-mask": names[2] = "data.zskipip"; break;
            case "other-container": "PK\u0003\u0004"u8.CopyTo(parts[1]); break;
            case "incorrect-disk-count": parts[2][^18] = 3; break;
            case "wrong-filename": parts[2][central + 46] ^= 1; break;
            case "wrong-size": parts[2][central + 46 + 1 + 4 + 8] ^= 1; break;
            case "wrong-zip64-offset": parts[2][^34] ^= 1; break;
            case "canonical": names = ["data.z01", "data.z02", "data.zip"]; break;
        }
        Assert.IsNull(await Inspect(fixture.Input(names, parts)));
    }

    // Build a single stored member with the system ZIP writer, then split on disk
    // boundaries and make catalog offsets disk-relative (PKWARE APPNOTE 4.3/8.5).
    private static byte[][] SingleFileZipDisks(bool zip64)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var data = zip.CreateEntry("a", CompressionLevel.NoCompression).Open())
            data.Write(new byte[400]);
        var bytes = stream.ToArray();
        var end = bytes.Length - 22;
        var catalog = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16));
        var central = bytes[catalog..end];
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), 4);
        using var split = new MemoryStream();
        using var writer = new BinaryWriter(split);
        writer.Write(0x08074b50); writer.Write(bytes.AsSpan(0, catalog));
        const int diskSize = 208;
        var catalogOffset = (int)split.Length - 2 * diskSize;
        if (zip64)
        {
            Array.Resize(ref central, central.Length + 20);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), uint.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), uint.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(30), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(47), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(49), 16);
            BinaryPrimitives.WriteUInt64LittleEndian(central.AsSpan(51), 400);
            BinaryPrimitives.WriteUInt64LittleEndian(central.AsSpan(59), 400);
        }
        writer.Write(central);
        if (zip64)
        {
            writer.Write(0x06064b50); writer.Write(44L); writer.Write(45); writer.Write(2); writer.Write(2);
            writer.Write(1L); writer.Write(1L); writer.Write((long)central.Length); writer.Write((long)catalogOffset);
            writer.Write(0x07064b50); writer.Write(2); writer.Write((long)catalogOffset + central.Length); writer.Write(3);
        }
        writer.Write(0x06054b50); writer.Write((ushort)2); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write((ushort)1); writer.Write(central.Length); writer.Write(catalogOffset); writer.Write((ushort)0);
        var archive = split.ToArray();
        return [archive[..diskSize], archive[diskSize..(2 * diskSize)], archive[(2 * diskSize)..]];
    }
}
