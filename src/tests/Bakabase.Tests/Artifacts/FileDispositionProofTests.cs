using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public class FileDispositionProofTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task Archive_and_unknown_key_are_both_required_for_cleanup()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("game.zip", "zip"), ("license.key", "key")));
        var result = Process(store, "result", source, Manifest(("game/run.exe", "exe"), ("license.key", "key")),
            [new("game.zip", ["game/run.exe"], true), new("license.key", ["license.key"])]);
        var final = Publish(store, "final", result);
        var coverage = await ArtifactFileCoverage.EvaluateAsync(store, source, [final.Id]);
        Assert.IsTrue(coverage.Complete);
        Assert.AreEqual("replaced", coverage.Files.Single(f => f.RelativePath == "game.zip").Disposition);
        Assert.AreEqual("preserved", coverage.Files.Single(f => f.RelativePath == "license.key").Disposition);

        var missing = Process(store, "missing", source, Manifest(("game/run.exe", "exe")),
            [new("game.zip", ["game/run.exe"], true)]);
        var incomplete = await ArtifactFileCoverage.EvaluateAsync(store, source, [Publish(store, "incomplete", missing).Id]);
        Assert.IsFalse(incomplete.Complete);
        Assert.AreEqual("unknown", incomplete.Files.Single(f => f.RelativePath == "license.key").Disposition);
    }

    [TestMethod]
    public async Task Every_replacement_child_must_reach_delivery_through_dynamic_boundaries()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("outer.zip", "outer")));
        var boundary = Process(store, "boundary", source, Manifest(("inner.zip", "inner"), ("key", "key")),
            [new("outer.zip", ["inner.zip", "key"], true)]);
        var nested = Process(store, "nested", boundary, Manifest(("run.exe", "exe"), ("key", "key")),
            [new("inner.zip", ["run.exe"], true), new("key", ["key"])]);
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(store, source, [Publish(store, "good", nested).Id])).Complete);
        var missing = Record(store, "missing", Manifest(("run.exe", "exe")), "publishedFiles", nested.Id);
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [missing.Id])).Complete,
            "An archive cannot be retired when just one of its descendants survives.");
    }

    [TestMethod]
    public async Task Separate_final_branches_can_together_cover_all_replacement_children()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("archive.zip", "zip")));
        var split = Process(store, "split", source, Manifest(("a", "a"), ("b", "b")),
            [new("archive.zip", ["a", "b"], true)]);
        var a = Record(store, "a", Manifest(("a", "a")), "publishedFiles", split.Id);
        var b = Record(store, "b", Manifest(("b", "b")), "publishedFiles", split.Id);
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(store, source, [a.Id, b.Id])).Complete);
    }

    [TestMethod]
    public async Task Legacy_retention_requires_same_path_as_well_as_same_bytes()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("first/config", "same"), ("second/config", "same")));
        var missing = Record(store, "missing", Manifest(("first/config", "same")), "publishedFiles", source.Id);
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [missing.Id])).Complete);
        var retained = Record(store, "retained", source.Manifest, "publishedFiles", source.Id);
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(store, source, [retained.Id])).Complete);
    }

    [TestMethod]
    public async Task Forged_prompt_evidence_and_untrusted_operation_do_not_authorize_deletion()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("archive.zip", "zip")));
        var output = Process(store, "output", source, Manifest(("result", "result")), [new("archive.zip", ["result"], true)]);
        var validMetadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(output.MetadataJson!, Json)!;
        store.Records[output.Id] = output with {MetadataJson = JsonSerializer.Serialize(validMetadata with
            {PlanJson = output.MetadataJson!, FileDisposition = null}, Json)};
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [output.Id])).Complete);
        store.Records[output.Id] = output;
        store.Kinds[output.Id] = "llm.proposedFiles";
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [output.Id])).Complete);
    }

    [TestMethod]
    public async Task Changed_manifest_and_missing_execution_proof_are_conservative()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("archive.zip", "zip")));
        var output = Process(store, "output", source, Manifest(("result", "result")), [new("archive.zip", ["result"], true)]);
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(output.MetadataJson!, Json)!;
        store.Records[output.Id] = output with {MetadataJson = JsonSerializer.Serialize(metadata with
            {FileDisposition = metadata.FileDisposition! with {InputFingerprint = "wrong"}}, Json)};
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [output.Id])).Complete);
        var proof = FileDispositionProofs.Create(source, output.Manifest, null);
        Assert.AreEqual("unknown", proof.Entries.Single().Disposition);
        proof = FileDispositionProofs.Create(source, output.Manifest,
            new([new("archive.zip", ["result"], true)], Manifest(("archive.zip", "changed private bytes")), output.Manifest));
        Assert.AreEqual("unknown", proof.Entries.Single().Disposition);
    }

    [TestMethod]
    public void Empty_replacements_and_unmeasured_composition_cannot_discard_inputs()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("archive.zip", "zip")));
        var middle = Manifest(("a", "a"), ("b", "b"));
        var final = Manifest(("a", "a"));
        var first = new FileLineage([new("archive.zip", ["a", "b"], true)], source.Manifest, middle);
        var second = new FileLineage([new("a", ["a"])], middle, final);
        var composed = FileDispositionProofs.Compose(first, second);
        Assert.IsNotNull(composed);
        Assert.AreEqual("unknown", FileDispositionProofs.Create(source, final, composed).Entries.Single().Disposition);
        Assert.IsNull(FileDispositionProofs.Compose(first, second with {InputManifest = null}));
        Assert.AreEqual("unknown", FileDispositionProofs.Create(source, final,
            new([new("archive.zip", [], true)], source.Manifest, final)).Entries.Single().Disposition);
    }

    [TestMethod]
    public async Task Trusted_rename_maps_exact_content_to_new_path_without_needing_intermediate_files()
    {
        var store = new Store();
        var source = Record(store, "source", Manifest(("opaque.bin", "same")));
        var output = Process(store, "renamed", source, Manifest(("game.zip", "same")), [new("opaque.bin", ["game.zip"])]);
        var final = Publish(store, "final", output);
        // These ledger-only fixtures deliberately have no files on disk. The read-only proof
        // remains usable after private intermediate payloads have been reclaimed.
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(store, source, [final.Id])).Complete);
    }

    private static ArtifactManifest Manifest(params (string Path, string Value)[] files)
    {
        var entries = files.Select(f => new ArtifactManifestEntry(f.Path, System.Text.Encoding.UTF8.GetByteCount(f.Value),
            ArtifactManifestFiles.Hash(f.Value))).OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        return new(entries, ArtifactManifestFiles.Fingerprint(entries));
    }

    private static ArtifactRecord Record(Store store, string id, ArtifactManifest manifest, string kind = "downloadedFiles", string? parent = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "file-proof-ledger-only", id);
        var record = new ArtifactRecord(id, id, kind, root, manifest.Entries.Select(e => Path.Combine(root, e.RelativePath)).ToArray(),
            manifest, manifest.Fingerprint, ArtifactLocationState.Available, true, parent == null ? [] : [parent], null);
        store.Records[id] = record;
        store.Kinds[id] = kind == "processedFiles" ? "files.process" : kind == "publishedFiles" ? "files.publish" : "files.download";
        return record;
    }

    private static ArtifactRecord Process(Store store, string id, ArtifactRecord input, ArtifactManifest output,
        IReadOnlyList<FileLineageEntry> entries)
    {
        var proof = FileDispositionProofs.Create(input, output, new(entries, input.Manifest, output));
        var record = Record(store, id, output, "processedFiles", input.Id) with
        {
            MetadataJson = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id, "{}", null,
                FileDisposition: proof), Json)
        };
        store.Records[id] = record; return record;
    }

    private static ArtifactRecord Publish(Store store, string id, ArtifactRecord input)
    {
        var record = Record(store, id, input.Manifest, "publishedFiles", input.Id) with
        {
            MetadataJson = JsonSerializer.Serialize(new FileArtifactPublicationMetadata("source", input.Id, input.OperationId,
                null, FileDispositionProofs.Preserve(input, input.Manifest)), Json)
        };
        store.Records[id] = record; return record;
    }

    private sealed class Store : IArtifactOperationStore
    {
        public Dictionary<string, ArtifactRecord> Records { get; } = [];
        public Dictionary<string, string> Kinds { get; } = [];
        public Task<ArtifactRecord?> GetArtifactAsync(string id, CancellationToken ct = default) => Task.FromResult(Records.GetValueOrDefault(id));
        public Task<ArtifactOperationDbModel?> GetOperationAsync(string id, CancellationToken ct = default) => Task.FromResult<ArtifactOperationDbModel?>(
            Records.TryGetValue(id, out var artifact) ? new() {Id = id, Kind = Kinds[id], State = ArtifactOperationState.Completed,
                OutputArtifactId = id, InputFingerprint = artifact.ParentArtifactIds.Count == 1 ? Records[artifact.ParentArtifactIds[0]].Fingerprint : artifact.Fingerprint} : null);
        public Task<ArtifactRecord?> ResolveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactRecord?> ResolveByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactOperationClaim> ClaimAsync(ArtifactOperationRequest request, string owner, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ArtifactOperationLease lease, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactRecord> CompleteAsync(ArtifactOperationLease lease, ArtifactRegistration registration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> FailAsync(ArtifactOperationLease lease, string error, bool unknown = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactRecord> AdoptAsync(ArtifactOperationRequest request, ArtifactRegistration registration, string owner, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AttachConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RenewExistingConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> PinConsumerAsync(string id, string key, string ownerKind, string ownerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseConsumerAsync(string id, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveConsumersAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryQuarantineAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetLocationAsync(string id, string root, ArtifactLocationState state, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
