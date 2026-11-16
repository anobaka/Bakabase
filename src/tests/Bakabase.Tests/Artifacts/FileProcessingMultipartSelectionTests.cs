using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingMultipartSelectionTests
{
    private string root = null!;
    private string input = null!;
    private string state = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "multipart-selection-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); state = Path.Combine(root, "state");
        Directory.CreateDirectory(input);
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [DataTestMethod]
    [DataRow("bundle.part1.exe", "bundle.part2.rar", false)]
    [DataRow("bundle.part1.exe", "bundle.part2.rar", true)]
    [DataRow("bundle.part1.rar", "bundle.part2.rar", false)]
    [DataRow("bundle.7z.001", "bundle.7z.002", false)]
    [DataRow("bundle.zip", "bundle.z01", false)]
    public async Task Selecting_one_entry_consumes_only_its_verified_family_and_preserves_internal_structure(
        string firstName, string secondName, bool currentDirectory)
    {
        var first = await Input("source/" + firstName);
        var second = await Input("source/" + secondName);
        var key = await Input("source/license.key");
        var other = await Input("other.part1.rar");
        var otherPart = await Input("other.part2.rar");
        var nearby = await Input("source/bundle.part3.rar"); // Exists on disk, absent from the authorized manifest.
        var archives = new Archives();
        var plan = Plan("source/" + firstName, currentDirectory);
        var executor = new FileProcessingPlanExecutor(archives);
        var result = await executor.ExecuteAsync(plan, input, [first, second, key, other, otherPart], state);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.Requests.Count);
        CollectionAssert.AreEqual(new[] {firstName, secondName}, archives.Requests.Single().Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual(firstName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0, archives.FormatCalls.Count);
        var output = "source/" + (currentDirectory ? "" : "bundle/") + "original-package/payload.bin";
        CollectionAssert.AreEquivalent(new[] {output, "source/license.key", "other.part1.rar", "other.part2.rar"},
            result.Files.Select(path => Path.GetRelativePath(result.OutputDirectory!, path).Replace('\\', '/')).ToArray());
        foreach (var name in new[] {firstName, secondName})
        {
            var lineage = result.FileLineage!.Entries.Single(entry => entry.InputRelativePath == "source/" + name);
            Assert.IsTrue(lineage.Replaced);
            CollectionAssert.AreEqual(new[] {output}, lineage.OutputRelativePaths.ToArray());
        }
        Assert.IsFalse(result.FileLineage!.Entries.Single(entry => entry.InputRelativePath == "source/license.key").Replaced);
        Assert.IsTrue(File.Exists(nearby));
        var checkpoint = await ReadState();
        var operation = checkpoint.Operations.Values.Single(value => value.ArchiveInputs != null);
        Assert.AreEqual(2, operation.ArchiveInputs!.Count);
        Assert.IsTrue(operation.ArchiveInputs.All(checkpoint.FileTransitions.ContainsKey));
        // The next assessment only sees the final manifest: consumed later volumes do not
        // survive merely because the model's operation named their entry file.
        Assert.IsFalse(result.FileLineage.OutputManifest!.Entries.Any(entry =>
            entry.RelativePath.EndsWith(secondName, StringComparison.Ordinal)));
        var replay = await executor.ExecuteAsync(plan, input, [first, second, key, other, otherPart], state);
        Assert.AreEqual(1, archives.Requests.Count);
        Assert.AreEqual(JsonSerializer.Serialize(result.FileLineage, Json), JsonSerializer.Serialize(replay.FileLineage, Json));
    }

    [DataTestMethod]
    [DataRow("bundle.part2.rar")]
    [DataRow("bundle.part3.rar")]
    public async Task Missing_or_ambiguous_sfx_volumes_do_not_create_replacement_proof(string later)
    {
        var first = await Input("bundle.part1.exe");
        var second = await Input(later);
        var duplicate = later.EndsWith("2.rar", StringComparison.Ordinal) ? await Input("bundle.part1.rar") : null;
        var files = new[] {first, second}.Concat(duplicate == null ? [] : new[] {duplicate}).ToArray();
        var archives = new Archives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(Plan("bundle.part1.exe"), input, files, state);
        Assert.IsFalse(result.Completed);
        Assert.IsTrue(result.FailureCode is "MissingVolumes" or "ArchiveInputRequired", result.FailureCode);
        Assert.AreEqual(0, archives.Requests.Count);
        var checkpoint = await ReadState();
        Assert.IsFalse(checkpoint.Operations.Values.Any(operation => operation.Completed));
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task Failed_extraction_keeps_every_volume_without_replacement_transitions()
    {
        var first = await Input("bundle.part1.exe"); var second = await Input("bundle.part2.rar");
        var archives = new Archives {FailExtraction = true};
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new FileProcessingPlanExecutor(archives)
            .ExecuteAsync(Plan("bundle.part1.exe"), input, [first, second], state));
        var checkpoint = await ReadState();
        Assert.IsFalse(checkpoint.Operations.Values.Any(operation => operation.Completed));
        Assert.IsTrue(checkpoint.FileTransitions.Values.All(value => value.Count == 0));
        Assert.IsTrue(File.Exists(first) && File.Exists(second));
    }

    [TestMethod]
    public async Task A_complete_named_Sfx_family_still_respects_unfinished_download_markers()
    {
        var first = await Input("bundle.part1.exe"); var second = await Input("bundle.part2.rar");
        await Input("bundle.part2.rar.aria2");
        var error = Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([first, second]));
        StringAssert.Contains(error.Message, "still downloading");
    }

    [TestMethod]
    public async Task A_manual_header_password_retries_the_same_bound_Sfx_family_without_losing_its_members()
    {
        var first = await Input("bundle.part1.exe"); var second = await Input("bundle.part2.rar");
        var archives = new Archives {RequiresHeaderPassword = true};
        var executor = new FileProcessingPlanExecutor(archives); var plan = Plan("bundle.part1.exe");
        var waiting = await executor.ExecuteAsync(plan, input, [first, second], state);
        Assert.IsFalse(waiting.Completed);
        Assert.AreEqual(ArchiveFormatProbeFailureCodes.PasswordRequired, waiting.FailureCode);
        Assert.AreEqual(0, archives.Requests.Count);
        var completed = await executor.ExecuteAsync(plan, input, [first, second], state, password: "fixture-password");
        Assert.IsTrue(completed.Completed, completed.Message);
        Assert.AreEqual(2, archives.Requests.Single().Files.Count);
        Assert.IsTrue(completed.FileLineage!.Entries.All(entry => entry.Replaced));
    }

    [TestMethod]
    public async Task Password_wait_does_not_adopt_a_new_unregistered_sibling_volume_from_the_private_snapshot()
    {
        var first = await Input("bundle.part1.exe"); var second = await Input("bundle.part2.rar");
        var archives = new Archives {RejectPassword = true};
        var executor = new FileProcessingPlanExecutor(archives); var plan = Plan("bundle.part1.exe");
        var waiting = await executor.ExecuteAsync(plan, input, [first, second], state);
        Assert.IsFalse(waiting.Completed); Assert.IsTrue(waiting.NeedsPassword);
        var checkpoint = await ReadState();
        var staged = checkpoint.Outputs["download"].Single(file => file.RelativePath == "bundle.part1.exe").Path;
        var added = Path.Combine(Path.GetDirectoryName(staged)!, "bundle.part3.rar");
        await File.WriteAllTextAsync(added, "unregistered additional volume");
        archives.RejectPassword = false;
        var retried = await executor.ExecuteAsync(plan, input, [first, second], state, password: "fixture-password");
        Assert.IsFalse(retried.Completed);
        Assert.AreEqual("ArchiveGroupInputChanged", retried.FailureCode);
        Assert.AreEqual(0, archives.Requests.Count);
        Assert.IsNull(retried.FileLineage);
        checkpoint = await ReadState();
        Assert.IsFalse(checkpoint.Operations.Values.Any(operation => operation.Completed));
        Assert.IsTrue(checkpoint.FileTransitions.Values.All(value => value.Count == 0));
        Assert.IsTrue(new[] {first, second, staged, added}.All(File.Exists));
    }

    [TestMethod]
    public async Task An_added_sibling_after_password_probe_is_blocked_before_extraction()
    {
        var first = await Input("bundle.part1.exe"); var second = await Input("bundle.part2.rar");
        string? added = null;
        var archives = new Archives {BeforePasswordSuccess = entry =>
        {
            added = Path.Combine(Path.GetDirectoryName(entry)!, "bundle.part3.rar");
            File.WriteAllText(added, "new unregistered volume");
        }};
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(Plan("bundle.part1.exe"), input, [first, second], state);
        Assert.IsFalse(result.Completed); Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(0, archives.Requests.Count); Assert.IsNull(result.FileLineage);
        Assert.IsTrue(File.Exists(added));
        Assert.IsFalse((await ReadState()).Operations.Values.Any(operation => operation.Completed));
    }

    private static FileProcessingPlan Plan(string selector, bool current = false) => new()
    {
        Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = selector,
            ExtractionLayout = current ? FileExtractionLayouts.CurrentDirectory : FileExtractionLayouts.ArchiveDirectory}]
    };

    private async Task<string> Input(string relative)
    {
        var path = Path.Combine(input, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, relative); return path;
    }

    private async Task<FileProcessingPlanExecutor.State> ReadState() =>
        JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Directory.EnumerateFiles(state, "state.json", SearchOption.AllDirectories).Single()), Json)!;

    private sealed class Archives : IArchiveExtractionService
    {
        public bool FailExtraction;
        public bool RequiresHeaderPassword;
        public bool RejectPassword;
        public Action<string>? BeforePasswordSuccess;
        public List<string> FormatCalls { get; } = [];
        public List<ArchiveExtractionRequest> Requests { get; } = [];
        public Task<string?> ProbeFormatAsync(string path, CancellationToken ct)
        { FormatCalls.Add(path); return Task.FromResult<string?>(".rar"); }
        public async Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string path,
            IReadOnlyList<string?> passwords, CancellationToken ct)
        {
            if (RequiresHeaderPassword && !passwords.Contains("fixture-password"))
                throw new ArchiveFormatProbeIncompleteException("Fixture header requires a password.", ArchiveFormatProbeFailureCodes.PasswordRequired);
            return new(await ProbeFormatAsync(path, ct));
        }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string path, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct)
        {
            if (!RejectPassword) BeforePasswordSuccess?.Invoke(path);
            return Task.FromResult(new ArchivePasswordProbe(!RejectPassword, null, [], "", "") {PasswordRejected = RejectPassword});
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Requests.Add(request);
            if (FailExtraction) return new(false, request.Directory, "Fixture extraction failed.");
            var original = Path.Combine(request.Directory, "original-package"); Directory.CreateDirectory(original);
            await File.WriteAllTextAsync(Path.Combine(original, "payload.bin"), "verified payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
