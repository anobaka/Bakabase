using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class LyricDeliveryAssessmentTests
{
    private string root = null!;
    private static readonly byte[] Mp3 = [255, 251, 144, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "lrc-delivery-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);
    private async Task<string> Write(string name, byte[] bytes)
    {
        var file = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, bytes); return file;
    }
    private ArtifactRecord Artifact(params string[] paths)
    {
        var entries = paths.Select(p => new ArtifactManifestEntry(Path.GetRelativePath(root, p), new FileInfo(p).Length, new string('0', 64))).ToArray();
        return new("artifact", "operation", "processedFiles", root, paths,
            new(entries, ArtifactManifestFiles.Fingerprint(entries)), "fixture", ArtifactLocationState.Available, true, [], null);
    }

    [TestMethod]
    public async Task Album_with_same_name_timed_lyrics_is_delivery_without_archive_probes()
    {
        var files = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            files.Add(await Write(i + ".MP3", Mp3));
            files.Add(await Write(i + ".lrc", "\ufeff[ti:Fixture]\n[00:01.23][01:12.000]Timed text\n[03:59]Other line"u8.ToArray()));
        }
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(files.ToArray()), default, extractionWasReviewed: true);
        Assert.AreEqual(0, result.Files.Count); Assert.IsFalse(result.Automatic);
    }

    [TestMethod]
    [DataRow("different.mp3")]
    [DataRow("other/song.mp3")]
    public async Task Unmatched_lyrics_remain_unknown_even_if_other_audio_exists(string audio)
    {
        var file = await Write("song.lrc", "[00:01.23]Text\n[00:02.00]Next"u8.ToArray());
        var other = await Write(audio, Mp3);
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(file, [file, other], default));
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(file, other), default, extractionWasReviewed: true)).Uncertain);
    }

    [TestMethod]
    public async Task DetachedLyricsMatchAWholeTrackSetAndSharedDiscFolders()
    {
        var timed = "[00:01.23]Text\n[00:02.00]Next"u8.ToArray();
        var one = await Write("translations/disc A/scene.lrc", timed);
        var two = await Write("translations/disc B/scene.lrc", timed);
        var first = await Write("media/mp3/disc A/scene.mp3", Mp3);
        var second = await Write("media/mp3/disc B/scene.mp3", Mp3);
        var most = new[] {one, two, first};
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(one, most, default));
        var full = most.Append(second).ToArray();
        Assert.IsTrue(await LyricDeliveryProbe.IsCompanionAsync(one, full, default));
        Assert.IsTrue(await LyricDeliveryProbe.IsCompanionAsync(two, full, default));
        Assert.IsFalse((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(full), default, extractionWasReviewed: true)).Uncertain);
        File.WriteAllBytes(second, new byte[50]);
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(full), default, extractionWasReviewed: true)).Uncertain);
    }

    [TestMethod]
    public async Task Bad_UTF8_missing_timestamps_and_implausible_seconds_do_not_pass()
    {
        var audio = await Write("song.mp3", Mp3);
        foreach (var bytes in new[] {new byte[] {255, 255}, "plain text"u8.ToArray(), "[00:99.00]Text"u8.ToArray(),
                     "[ar:Metadata only]"u8.ToArray(), "[00:01.00]Text\0"u8.ToArray()})
        {
            var lyric = await Write("song.lrc", bytes);
            Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
        }
    }

    [TestMethod]
    public async Task Matching_audio_requires_real_audio_structure_and_is_manifest_bound()
    {
        var lyric = await Write("song.lrc", "[00:01.23]Text\n[00:02.00]Next"u8.ToArray());
        var audio = await Write("song.mp3", new byte[40]);
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
        await File.WriteAllBytesAsync(audio, Mp3);
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [lyric], default), "Unregistered siblings are never discovered.");
        var id3 = new byte[] {73, 68, 51, 3, 0, 0, 0, 0, 0, 7}.Concat(new byte[7]).Concat(Mp3).ToArray();
        await File.WriteAllBytesAsync(audio, id3);
        Assert.IsTrue(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
        id3[17] = 0; await File.WriteAllBytesAsync(audio, id3);
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default), "ID3 metadata alone is insufficient.");
    }

    [TestMethod]
    public async Task Oversized_LRC_and_unknown_sibling_are_retained_for_review()
    {
        var audio = await Write("song.mp3", Mp3);
        var lyric = await Write("song.lrc", new byte[LyricDeliveryProbe.MaximumBytes + 1]);
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
        await File.WriteAllBytesAsync(lyric, "[00:01.23]Text\n[00:02.00]Next"u8.ToArray());
        var unknown = await Write("keep.key", "key"u8.ToArray());
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(audio, lyric, unknown), default, extractionWasReviewed: true)).Uncertain);
        Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(File.Exists(lyric));
    }

    [TestMethod]
    public async Task A_small_lyric_typo_is_retained_but_mostly_unrelated_text_does_not_pass()
    {
        var audio = await Write("song.mp3", Mp3);
        var valid = string.Join('\n', Enumerable.Range(0, 10).Select(i => $"[00:{i:00}.20]Timed text"));
        var bytes = System.Text.Encoding.UTF8.GetBytes(valid + "\n[00:11.20Missing bracket");
        var lyric = await Write("song.lrc", bytes);
        Assert.IsTrue(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(lyric), "Recognition must never repair, rewrite or remove authored text.");
        await File.WriteAllTextAsync(lyric, valid + "\nUnrelated note\nMore unrelated text");
        Assert.IsFalse(await LyricDeliveryProbe.IsCompanionAsync(lyric, [audio, lyric], default));
    }
}
