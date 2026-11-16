using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bakabase.Service.Models.View.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Tests;

[TestClass]
public sealed class CompressedFileDetectionSelectionTests
{
    private string root = null!;

    [TestInitialize]
    public void Setup() => root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "archive-selection-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [DataTestMethod]
    [DataRow("selected.pdf", false)]
    [DataRow("selected.pdf", true)]
    [DataRow("selected.jpg", false)]
    [DataRow("selected.jpg", true)]
    [DataRow("selected", false)]
    [DataRow("selected", true)]
    [DataRow("selected.zip", false)]
    [DataRow("selected.zip", true)]
    public async Task ExplicitFilesReachTheArchiveToolRegardlessOfExtensionOrDirectoryScanOption(
        string name, bool includeUnknownFiles)
    {
        var selected = Fixture(name);
        Fixture("unselected.jpg");
        var probe = new RecordingArchiveService();
        var updates = await Detect([selected], includeUnknownFiles, probe);

        CollectionAssert.AreEqual(new[] {selected}, probe.Calls.ToArray());
        var final = updates.Last();
        Assert.AreEqual(CompressedFileDetectionResultStatus.Complete, final.Status);
        Assert.AreEqual(root, Path.GetFullPath(final.Directory!));
        CollectionAssert.AreEqual(new[] {name}, final.Files!);
        Assert.AreEqual("synthetic container accepted", final.Message);
        Assert.IsTrue(File.Exists(selected), "Detection must not rename or consume the selected file.");
        Assert.AreEqual(2, Directory.GetFiles(root).Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DirectoryDiscoveryKeepsItsOptInForUnknownFiles(bool includeUnknownFiles)
    {
        var known = Fixture("archive.zip");
        var unknown = Fixture("image.jpg");
        var probe = new RecordingArchiveService();
        var updates = await Detect([root], includeUnknownFiles, probe);
        var expected = includeUnknownFiles ? new[] {known, unknown} : [known];

        CollectionAssert.AreEquivalent(expected, probe.Calls.ToArray());
        CollectionAssert.AreEquivalent(expected, updates.Where(u => u.Status == CompressedFileDetectionResultStatus.Complete)
            .SelectMany(u => u.Files!.Select(file => Path.GetFullPath(Path.Combine(u.Directory!, file)))).Distinct().ToArray());
        Assert.AreEqual(2, Directory.GetFiles(root).Length);
    }

    [TestMethod]
    public async Task ExplicitSelectionStillHonorsStorageBoundaryBeforeProbing()
    {
        var probe = new RecordingArchiveService();
        await Assert.ThrowsExactlyAsync<IOException>(() => Detect(
            [Path.Combine(root, "..", "outside.pdf")], true, probe));
        Assert.AreEqual(0, probe.Calls.Count);
    }

    private string Fixture(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "inert fixture; the recording archive service owns format recognition");
        return path;
    }

    private async Task<CompressedFileDetectionResultViewModel[]> Detect(string[] paths, bool includeUnknownFiles,
        RecordingArchiveService probe)
    {
        using var body = new MemoryStream();
        var http = new DefaultHttpContext();
        http.Response.Body = body;
        var controller = new FileController(null!, null!, null!, probe, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, new Storage(root))
        {
            ControllerContext = new ControllerContext {HttpContext = http}
        };
        await controller.DetectCompressedFiles(new CompressedFileDetectionInputModel
        {
            Paths = paths, IncludeUnknownFiles = includeUnknownFiles
        });
        Assert.AreEqual("application/x-ndjson", http.Response.ContentType);
        return Encoding.UTF8.GetString(body.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<CompressedFileDetectionResultViewModel>(line,
                JsonSerializerOptions.Web)!).ToArray();
    }

    private sealed class RecordingArchiveService : IArchiveExtractionService
    {
        public List<string> Calls { get; } = [];
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add(entryFile);
            return Task.FromResult(new ArchivePasswordProbe(true, null, [], "T payload.txt\n",
                "synthetic container accepted"));
        }

        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct) => throw new AssertFailedException("Detection must not extract files.");
    }

    private sealed class Storage(string root) : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            [new(root, "Fixtures", "local", false)];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            var full = Path.GetFullPath(path);
            return full == root || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!IsPathAllowed(path, purpose)) throw new IOException("Outside the fixture storage root.");
        }
        public void EnsureTreeMutationAllowed(string path) => throw new AssertFailedException("Detection must not mutate trees.");
    }
}
