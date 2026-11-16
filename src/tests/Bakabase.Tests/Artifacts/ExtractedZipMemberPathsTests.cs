using System.IO.Compression;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ExtractedZipMemberPathsTests
{
    private string root = null!;
    private string output = null!;

    [TestInitialize] public void Setup()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows already interprets the path separator.");
        root = Directory.CreateTempSubdirectory("bakabase-zip-paths-").FullName;
        output = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;
    }

    [TestCleanup] public void Cleanup() { if (root != null) Directory.Delete(root, true); }

    private string Zip(byte host, params string[] names)
    {
        var file = Path.Combine(root, "untitled.archive");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var name in names)
            {
                var entry = archive.CreateEntry(name);
                entry.ExternalAttributes = 0;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(name);
            }
        }
        var bytes = File.ReadAllBytes(file);
        for (var i = 0; i < bytes.Length - 46; i++)
            if (bytes.AsSpan(i, 4).SequenceEqual("PK\x01\x02"u8)) bytes[i + 5] = host;
        File.WriteAllBytes(file, bytes);
        return file;
    }

    [TestMethod]
    public async Task Full_windows_zip_catalog_and_matching_receipt_restore_only_the_separator()
    {
        const string member = @"音声\SEあり\01.wav", sibling = @"音声\SEなし\01.wav";
        var archive = Zip(0, member, sibling, "notes.txt");
        var literal = Path.Combine(output, member);
        var resumed = Path.Combine(output, sibling.Replace('\\', '/'));
        Directory.CreateDirectory(Path.GetDirectoryName(resumed)!);
        var unchanged = Path.Combine(output, "notes.txt");
        File.WriteAllText(literal, member);
        File.WriteAllText(resumed, sibling);
        File.WriteAllText(unchanged, "notes.txt");
        string[] files = [literal, resumed, unchanged];
        var expected = await ArtifactManifestFiles.CaptureAsync(output, files);
        Assert.IsTrue(await ExtractedZipMemberPaths.TryRestoreAsync(output, files, expected, [archive], default));
        Assert.IsFalse(File.Exists(literal));
        Assert.AreEqual(member, File.ReadAllText(Path.Combine(output, "音声", "SEあり", "01.wav")));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(output, expected)).Available);
    }

    [TestMethod]
    [DataRow(3, false, false)] // A Unix publisher may intentionally use a literal backslash.
    [DataRow(0, true, false)]
    [DataRow(0, false, true)]
    public async Task Unproved_payload_or_an_occupied_target_is_never_renamed(int host, bool changeBytes, bool occupy)
    {
        const string name = @"part\sample.txt";
        var archive = Zip((byte)host, name);
        var file = Path.Combine(output, name);
        File.WriteAllText(file, name);
        var manifest = await ArtifactManifestFiles.CaptureAsync(output, [file]);
        if (changeBytes) File.WriteAllText(file, new string('x', name.Length));
        var destination = Path.Combine(output, "part", "sample.txt");
        if (occupy)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, "unrelated");
        }
        Assert.IsFalse(await ExtractedZipMemberPaths.TryRestoreAsync(output, [file], manifest, [archive], default));
        Assert.IsTrue(File.Exists(file));
        if (occupy) Assert.AreEqual("unrelated", File.ReadAllText(destination));
        else Assert.IsFalse(File.Exists(destination));
    }
}
