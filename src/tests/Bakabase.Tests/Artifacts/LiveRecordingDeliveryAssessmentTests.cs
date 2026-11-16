using System.Buffers.Binary;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class LiveRecordingDeliveryAssessmentTests
{
    private string root = null!;
    private const string Comments = "<i><d p='0.5,1,25,16777215,100,0,23,0'>First</d><d p='1.2,1,25,16777215,101,0,71,0'>Second</d></i>";
    [TestInitialize] public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    private static byte[] Video()
    {
        // FLV header, zero previous-tag size, one video tag and matching previous-tag size.
        byte[] video = [0x46, 0x4c, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0,
            9, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0, 0x17, 1, 0, 0, 0, 7, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32BigEndian(video.AsSpan(video.Length - 4), 17);
        return video;
    }

    [DataTestMethod]
    [DataRow("paired", false)]
    [DataRow("empty chat", true)]
    [DataRow("wrong directory", true)]
    [DataRow("unsafe xml", true)]
    [DataRow("broken xml", true)]
    [DataRow("nested d", true)]
    [DataRow("extension only", true)]
    [DataRow("invalid tag size", true)]
    [DataRow("unpaired FLV", false)]
    [DataRow("unknown companion", true)]
    public async Task VerifiedRecordingsCanPassWithOptionalTimedComments(string variant, bool uncertain)
    {
        var files = new List<string>();
        void Add(string relative, byte[] content)
        {
            var file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, content); files.Add(file);
        }
        var video = Video();
        if (variant == "extension only") video[0] = 0;
        if (variant == "invalid tag size") video[^1] = 0;
        for (var part = 1; part <= 3; part++)
        {
            Add($"recording/part{part}.flv", video);
            if (variant != "unpaired FLV" || part != 3)
                Add((variant == "wrong directory" ? "unrelated/" : "recording/") + $"part{part}.xml",
                    Encoding.UTF8.GetBytes(variant switch
                    {
                        "empty chat" => "<i><note>not timed</note></i>",
                        "unsafe xml" => "<!DOCTYPE i [<!ENTITY sneaky SYSTEM 'file:///none'>]>" + Comments,
                        "broken xml" => Comments + "<",
                        "nested d" => "<i><extra>" + Comments + "</extra></i>",
                        _ => Comments
                    }));
        }
        Add("recording/readme.txt", "retain"u8.ToArray());
        if (variant == "unknown companion") Add("recording/extra.key", "retain"u8.ToArray());
        var entries = files.Select(f => new ArtifactManifestEntry(Path.GetRelativePath(root, f), new FileInfo(f).Length, new string('0', 64))).ToArray();
        var artifact = new ArtifactRecord("record", "operation", "processedFiles", root, files,
            new(entries, ArtifactManifestFiles.Fingerprint(entries)), "fixture", ArtifactLocationState.Available, true, [], null);
        var assessment = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, extractionWasReviewed: true);
        Assert.AreEqual(uncertain, assessment.Uncertain);
        Assert.IsFalse(assessment.Automatic);
        Assert.IsTrue(files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("picture", true)]
    [DataRow("config", false)]
    [DataRow("oversize", false)]
    [DataRow("missing picture", false)]
    [DataRow("invalid unit header", false)]
    public async Task LegacyHevcRequiresAFramedCodedPicture(string variant, bool expected)
    {
        // A recorder SEI followed by an intra-coded picture, both length-prefixed.
        byte[] payload = [0x1c, 1, 0, 0, 0, 0, 0, 0, 3, 0x50, 1, 0x80, 0, 0, 0, 3, 0x26, 1, 0x80];
        if (variant == "config") payload[1] = 0;
        if (variant == "oversize") payload[15] = 30;
        if (variant == "missing picture") payload[16] = 0x50;
        if (variant == "invalid unit header") payload[17] = 0;
        var file = Path.Combine(root, "clip.flv");
        var prefix = Video()[..24];
        prefix[16] = (byte)payload.Length;
        byte[] previousTagSize = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(previousTagSize, 11 + payload.Length);
        await File.WriteAllBytesAsync(file, [.. prefix, .. payload, .. previousTagSize]);
        Assert.AreEqual(expected, await LiveRecordingDeliveryProbe.AreRecordingsAsync([file], default));
    }
}
