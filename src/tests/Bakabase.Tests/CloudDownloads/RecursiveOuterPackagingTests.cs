using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RecursiveOuterPackagingTests
{
    private string root = null!, input = null!, state = null!;
    private readonly Archives archives = new();
    private static RecursiveArchiveOptions Limits => new() {MinimumFreeBytes = 0, MaxDepth = 3, MaxArchiveGroups = 3};
    private RecursiveArchiveExecutor Executor => new(archives);

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "outer-packaging-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); state = Path.Combine(root, "state"); Directory.CreateDirectory(input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> Input(string name, string marker, bool signature = true)
    {
        var path = Path.Combine(input, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, signature ? Archives.Zip(marker) : marker);
        return path;
    }

    [TestMethod]
    public async Task SingleSignedOuterLayersContinueAndRetainRuntimeLineage()
    {
        var outer = await Input("outer.zip", "outer");
        var result = await Executor.ExecuteAsync(input, [outer], state, Limits, onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEqual(new[] {"outer", "inner"}, archives.Calls.ToArray());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.AreEqual("file.txt", Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()));
        Assert.AreEqual(2, result.Lineage!.Count);
        var link = result.FileLineage!.Entries.Single();
        Assert.IsTrue(link.Replaced);
        Assert.AreEqual("outer.zip", link.InputRelativePath);
        Assert.AreEqual(1, link.OutputRelativePaths.Count);
        Assert.IsTrue(File.Exists(outer));
        Assert.IsTrue((await Executor.ExecuteAsync(input, [outer], state, Limits, onlyOuterPackaging: true)).Completed);
        Assert.AreEqual(2, archives.Calls.Count, "A committed result must not repeat extraction.");
    }

    [DataTestMethod]
    [DataRow("with-key")]
    [DataRow("multiple")]
    public async Task NewlyProducedSiblingFilesStopAutomaticRecursionWithoutDroppingAnyOutput(string marker)
    {
        var outer = await Input("outer.zip", marker);
        var result = await Executor.ExecuteAsync(input, [outer], state, Limits, onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(1, archives.Calls.Count);
        Assert.AreEqual(2, result.Files.Count);
        Assert.IsTrue(result.Files.Any(p => p.EndsWith("inner.zip")));
        Assert.AreEqual(2, result.FileLineage!.Entries.Single().OutputRelativePaths.Count);
        if (marker == "with-key") Assert.AreEqual("license", await File.ReadAllTextAsync(result.Files.Single(p => p.EndsWith("license.key"))));
    }

    [TestMethod]
    public async Task OuterPackagingRemovesOnlyGeneratedArchiveShellsAndKeepsRealInternalDirectories()
    {
        var outer = await Input("old-layout/outer.zip", "wrapped");
        var result = await Executor.ExecuteAsync(input, [outer], state, Limits, onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEqual(new[] {"wrapped", "with-real-folder"}, archives.Calls.ToArray());
        Assert.AreEqual(Path.Combine("InnerDir", "child.bin"), Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.AreEqual("InnerDir/child.bin", result.FileLineage!.Entries.Single().OutputRelativePaths.Single());
        Assert.IsTrue(File.Exists(outer));
    }

    [TestMethod]
    public async Task NarrowCandidateListCannotHideUnknownInputSibling()
    {
        var archive = await Input("outer.zip", "outer");
        var key = await Input("license.key", "license", signature: false);
        var result = await Executor.ExecuteAsync(input, [archive, key], state, Limits,
            archiveCandidates: [archive], onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, archives.Calls.Count);
        Assert.AreEqual(2, result.Files.Count);
        Assert.IsTrue(result.FileLineage!.Entries.All(e => !e.Replaced && e.OutputRelativePaths.Count == 1));
    }

    [DataTestMethod]
    [DataRow("currentDirectory", "sources/ArchiveInner/InnerDir/child.bin")]
    [DataRow("archiveDirectory", "sources/outer/ArchiveInner/wrapped/InnerDir/child.bin")]
    public async Task ExplicitLayoutInheritsAcrossOuterLayersAndPreservesMemberParents(string layout, string expected)
    {
        var archive = await Input("sources/outer.zip", "wrapped");
        var result = await Executor.ExecuteAsync(input, [archive], state, Limits, onlyOuterPackaging: true,
            extractionLayout: layout);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(expected, Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()).Replace('\\', '/'));
        Assert.IsFalse(result.DirectoryProvenance?.Entries.Any(e => e.GeneratedAncestors.Count != 0) == true);
    }

    [TestMethod]
    public async Task ArchiveLookingNameWithoutSignatureDoesNotGrantAutomaticExtraction()
    {
        var archive = await Input("misnamed.zip", "ordinary-file", signature: false);
        var result = await Executor.ExecuteAsync(input, [archive], state, Limits, onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, archives.Calls.Count);
        Assert.AreEqual("ordinary-file", await File.ReadAllTextAsync(result.Files.Single()));
    }

    [TestMethod]
    public async Task FrozenOuterModeCannotBeBroadenedAndLegacyMissingFlagKeepsDefaultBehavior()
    {
        var archive = await Input("outer.zip", "plain");
        Assert.IsTrue((await Executor.ExecuteAsync(input, [archive], state, Limits, onlyOuterPackaging: true)).Completed);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Executor.ExecuteAsync(input, [archive], state, Limits));
        var legacy = Path.Combine(root, "legacy");
        Assert.IsTrue((await Executor.ExecuteAsync(input, [archive], legacy, Limits)).Completed);
        var path = Path.Combine(legacy, "recursive-state.json");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject(); saved.Remove("onlyOuterPackaging");
        await File.WriteAllTextAsync(path, saved.ToJsonString());
        Assert.IsTrue((await Executor.ExecuteAsync(input, [archive], legacy, Limits)).Completed);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Executor.ExecuteAsync(input, [archive], legacy, Limits, onlyOuterPackaging: true));
        Assert.AreEqual(2, archives.Calls.Count);
    }

    [TestMethod]
    public async Task RepeatedOuterBytesWaitInsteadOfLooping()
    {
        var archive = await Input("outer.zip", "cycle");
        var result = await Executor.ExecuteAsync(input, [archive], state, Limits, onlyOuterPackaging: true);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("RepeatedContent", result.WaitReason);
        Assert.AreEqual(1, archives.Calls.Count);
        Assert.IsTrue(File.Exists(archive));
    }

    [TestMethod]
    public async Task DepthAndPasswordResumeKeepCompletedOuterExtraction()
    {
        var archive = await Input("outer.zip", "outer-locked");
        var first = await Executor.ExecuteAsync(input, [archive], state, Limits with {MaxDepth = 1}, onlyOuterPackaging: true);
        Assert.AreEqual("DepthLimit", first.WaitReason);
        var waiting = await Executor.ExecuteAsync(input, [archive], state, Limits, onlyOuterPackaging: true);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        Assert.AreEqual(1, archives.Calls.Count);
        var result = await Executor.ExecuteAsync(input, [archive], state, Limits,
            new(waiting.ArchiveId!, "correct"), onlyOuterPackaging: true);
        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEqual(new[] {"outer-locked", "locked"}, archives.Calls.ToArray());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.IsTrue(File.Exists(archive));
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public readonly List<string> Calls = [];
        public static string Zip(string marker) => "PK\u0003\u0004" + marker;
        private static async Task<string> Marker(string path, CancellationToken ct) => (await File.ReadAllTextAsync(path, ct))[4..];
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct)
        {
            var marker = await Marker(entry, ct); var password = candidates.FirstOrDefault(p => p == "correct");
            return new(marker != "locked" || password != null, password, [], "", "");
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            var marker = await Marker(request.Files[0], ct); Calls.Add(marker);
            Directory.CreateDirectory(request.Directory);
            var outputs = marker switch
            {
                "outer" => new Dictionary<string, string> {{"inner.zip", Zip("inner")}},
                "outer-locked" => new() {{"inner.zip", Zip("locked")}},
                "wrapped" => new() {{"ArchiveInner/wrapped.zip", Zip("with-real-folder")}},
                "with-real-folder" => new() {{"InnerDir/child.bin", "payload"}},
                "with-key" => new() {{"inner.zip", Zip("inner")}, {"license.key", "license"}},
                "multiple" => new() {{"inner.zip", Zip("inner")}, {"other.zip", Zip("plain")}},
                "cycle" => new() {{"again.zip", Zip("cycle")}},
                _ => new() {{"file.txt", "payload"}}
            };
            foreach (var (name, value) in outputs)
            {
                var path = Path.Combine(request.Directory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, value, ct);
            }
            progress?.Invoke(100); return new(true, request.Directory, "");
        }
    }
}
