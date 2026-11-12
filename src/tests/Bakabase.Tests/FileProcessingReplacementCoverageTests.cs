using System.Diagnostics;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public class FileProcessingReplacementCoverageTests
{
    public TestContext TestContext { get; set; } = null!;

    private static FileProcessingPlanExecutor.State State(int count)
    {
        var root = Path.Combine(Path.GetTempPath(), "coverage-metadata");
        var state = new FileProcessingPlanExecutor.State {LineageVersion = 1, ExecutionRoot = root};
        var entries = new List<ArtifactManifestEntry>();
        for (var i = 0; i < count; i++)
        {
            var relative = $"file-{i}.bin";
            var entry = new ArtifactManifestEntry(relative, 7, new string('a', 64));
            entries.Add(entry);
            state.Active.Add(new(Path.Combine(root, "outputs", relative), relative));
            state.FileContents["inputs/" + relative] = entry;
            state.FileContents["outputs/" + relative] = entry;
            state.FileTransitions["inputs/" + relative] = ["outputs/" + relative];
        }
        state.FinalManifest = new(entries, ArtifactManifestFiles.Fingerprint(entries));
        return state;
    }

    [TestMethod]
    public void Five_thousand_files_share_one_checkpoint_index_and_transition_proof()
    {
        var state = State(5000);
        var proof = new FileProcessingPlanExecutor.ReplacementCoverage(state, default);
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < 5000; i++)
                Assert.IsTrue(proof.IsComplete(Path.Combine(state.ExecutionRoot!, "inputs", $"file-{i}.bin")));
        Assert.AreEqual(5000, proof.IndexedFinalEntries);
        Assert.AreEqual(5000, proof.IndexedActiveEntries);
        Assert.AreEqual(10000, proof.VisitedEntries, "Each input/output is proved once, not once per selected file or pass.");
    }

    [TestMethod]
    [DataRow("complete", true)]
    [DataRow("missing-output", false)]
    [DataRow("changed-output", false)]
    [DataRow("unknown-input", false)]
    [DataRow("cycle", false)]
    [DataRow("empty-transition", false)]
    [DataRow("legacy", false)]
    public void Every_replacement_leaf_requires_recorded_matching_final_content(string scenario, bool expected)
    {
        var state = State(2);
        state.FileContents["inputs/archive.zip"] = new("archive.zip", 10, new string('b', 64));
        state.FileTransitions["inputs/archive.zip"] = ["outputs/file-0.bin", "outputs/file-1.bin"];
        switch (scenario)
        {
            case "missing-output": state.Active.RemoveAt(1); break;
            case "changed-output": state.FileContents["outputs/file-1.bin"] = new("file-1.bin", 8, new string('a', 64)); break;
            case "unknown-input": state.FileContents.Remove("inputs/archive.zip"); break;
            case "cycle": state.FileTransitions["inputs/archive.zip"] = ["inputs/file-0.bin"]; state.FileTransitions["inputs/file-0.bin"] = ["inputs/archive.zip"]; break;
            case "empty-transition": state.FileTransitions["inputs/archive.zip"] = []; break;
            case "legacy": state.LineageVersion = 0; break;
        }
        var proof = new FileProcessingPlanExecutor.ReplacementCoverage(state, default);
        Assert.AreEqual(expected, proof.IsComplete(Path.Combine(state.ExecutionRoot!, "inputs", "archive.zip")));
        Assert.IsFalse(proof.IsComplete(Path.Combine(state.ExecutionRoot!, "unregistered.key")));
    }

    [TestMethod]
    public void New_checkpoint_read_gets_fresh_proof_and_cancellation_is_observed()
    {
        var state = State(1);
        Assert.IsTrue(new FileProcessingPlanExecutor.ReplacementCoverage(state, default)
            .IsComplete(Path.Combine(state.ExecutionRoot!, "inputs/file-0.bin")));
        state.Active.Clear();
        Assert.IsFalse(new FileProcessingPlanExecutor.ReplacementCoverage(state, default)
            .IsComplete(Path.Combine(state.ExecutionRoot!, "inputs/file-0.bin")));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => new FileProcessingPlanExecutor.ReplacementCoverage(state, cancellation.Token));
    }

    // Opt-in, metadata-only local measurement. The ordinary suite uses the deterministic
    // 5k work-count test above; no production path or payload is touched by this benchmark.
    [TestMethod]
    public void Pinned_checkpoint_metadata_benchmark()
    {
        var directory = Environment.GetEnvironmentVariable("BAKABASE_COVERAGE_BENCHMARK");
        if (string.IsNullOrEmpty(directory)) return;
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(File.ReadAllText(
            Path.Combine(directory, "state.private.json")), JsonSerializerOptions.Web)!;
        using var context = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "context.private.json")));
        state.ExecutionRoot = context.RootElement.GetProperty("executionRoot").GetString();
        var paths = context.RootElement.GetProperty("candidatePaths").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var timer = Stopwatch.StartNew();
        var before = paths.Select(path => LegacyProof(state, path)).ToArray();
        var oldDuration = timer.Elapsed;
        timer.Restart();
        var proof = new FileProcessingPlanExecutor.ReplacementCoverage(state, default);
        var after = paths.Select(proof.IsComplete).ToArray();
        var newDuration = timer.Elapsed;
        CollectionAssert.AreEqual(before, after);
        TestContext.WriteLine($"Candidates={paths.Length}; old={oldDuration.TotalMilliseconds:F2}ms; indexed={newDuration.TotalMilliseconds:F2}ms; final={proof.IndexedFinalEntries}; active={proof.IndexedActiveEntries}; visited={proof.VisitedEntries}");
    }

    private static bool LegacyProof(FileProcessingPlanExecutor.State state, string path)
    {
        if (state.LineageVersion != 1 || state.FinalManifest == null) return false;
        var final = state.FinalManifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var visiting = new HashSet<string>(FileProcessingFiles.PathComparer);
        var answers = new Dictionary<string, bool>(FileProcessingFiles.PathComparer);
        string Key(string file) => ArtifactManifestFiles.RelativeFile(state.ExecutionRoot!, file);
        bool Visit(string candidate)
        {
            if (answers.TryGetValue(candidate, out var answer)) return answer;
            if (!state.FileContents.TryGetValue(candidate, out var contents) || !visiting.Add(candidate)) return false;
            var retained = state.Active.Any(a => FileProcessingFiles.PathComparer.Equals(Key(a.Path), candidate) &&
                final.TryGetValue(a.RelativePath.Replace('\\', '/'), out var output) && contents.Length == output.Length &&
                contents.Sha256.Equals(output.Sha256, StringComparison.OrdinalIgnoreCase));
            var covered = retained || state.FileTransitions.TryGetValue(candidate, out var outputs) && outputs.Count > 0 && outputs.All(Visit);
            visiting.Remove(candidate); return answers[candidate] = covered;
        }
        return Visit(Key(path));
    }
}
