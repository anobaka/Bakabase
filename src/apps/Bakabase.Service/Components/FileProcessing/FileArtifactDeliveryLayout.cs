using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Pure manifest projection. Does not inspect names, read payloads, move files or grant cleanup.</summary>
public static class FileArtifactDeliveryLayout
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Validate(ArtifactDeliveryPolicy? policy)
    {
        if (policy != null && policy.Version != 1) throw new ArgumentException("DELIVERY_POLICY_INVALID");
    }

    public static ArtifactDeliveryLayoutPreview Preview(ArtifactRecord input, ArtifactDeliveryPolicy? policy)
    {
        Validate(policy);
        var manifest = input.Manifest;
        if (input.Fingerprint != manifest.Fingerprint ||
            manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories))
            throw new InvalidDataException("The source manifest fingerprint changed.");
        var files = manifest.Entries.Select(e => e.RelativePath).ToArray();
        var directories = (manifest.Directories ?? []).ToArray();
        ValidatePaths(files, directories, policy != null);
        var evidence = ReadProvenance(input);
        var generatedCandidates = new List<string>();
        if (evidence != null)
        {
            var measured = evidence.Entries.SelectMany(e => e.GeneratedAncestors).GroupBy(p => p, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var actual = measured.Keys.ToDictionary(p => p, _ => 0, StringComparer.Ordinal);
            foreach (var (path, directory) in files.Select(p => (p, false)).Concat(directories.Select(p => (p, true))))
            {
                var parts = path.Split('/');
                for (var count = 1; count <= parts.Length - (directory ? 0 : 1); count++)
                {
                    var prefix = string.Join('/', parts.Take(count));
                    if (actual.ContainsKey(prefix)) actual[prefix]++;
                }
            }
            // Compare complete subtree membership without an O(files²) evidence search.
            generatedCandidates.AddRange(measured.Keys.Where(p => actual[p] > 0 && actual[p] == measured[p]));
        }
        var generated = policy?.RemoveGeneratedDirectories == true ? generatedCandidates : [];
        var generatedSet = generated.ToHashSet(StringComparer.Ordinal);
        string Project(string path) => RemoveAncestors(path, generatedSet);
        var projectedFiles = files.Select(Project).ToArray();
        var projectedDirectories = directories.Select(Project).ToArray();
        ValidatePaths(projectedFiles, projectedDirectories.Where(p => p.Length > 0), policy != null);
        string? singleRoot = null;
        if (policy?.CollapseSingleRootDirectory == true && projectedFiles.Length > 0 && projectedFiles.All(p => p.Contains('/')))
        {
            var roots = projectedFiles.Select(p => p.Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
            var remainingGenerated = generatedCandidates.Except(generated, StringComparer.Ordinal).Select(Project).ToArray();
            if (roots.Length == 1 && !remainingGenerated.Contains(roots[0], StringComparer.Ordinal) &&
                projectedDirectories.All(p => p.Length == 0 || Within(p, roots[0], true)))
                singleRoot = roots[0];
        }
        string Collapse(string path) => singleRoot == null || path.Length == 0 ? path :
            path == singleRoot ? "" : path[(singleRoot.Length + 1)..];
        projectedFiles = projectedFiles.Select(Collapse).ToArray();
        projectedDirectories = projectedDirectories.Select(Collapse).ToArray();
        ValidatePaths(projectedFiles, projectedDirectories.Where(p => p.Length > 0), policy != null);
        var mappings = files.Select((p, i) => new ArtifactDeliveryPathMapping(p, projectedFiles[i])).ToArray();
        var directoryMappings = directories.Select((p, i) => new ArtifactDeliveryPathMapping(p, projectedDirectories[i])).ToArray();
        var entries = manifest.Entries.Select((e, i) => e with {RelativePath = projectedFiles[i]})
            .OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        var outputDirectories = projectedDirectories.Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        // Preserve null versus [] for an unchanged legacy manifest's fingerprint.
        IReadOnlyList<string>? storedDirectories = manifest.Directories == null && outputDirectories.Length == 0 ? null : outputDirectories;
        var output = new ArtifactManifest(entries, ArtifactManifestFiles.Fingerprint(entries, storedDirectories), storedDirectories);
        var changed = mappings.Any(m => m.SourceRelativePath != m.TargetRelativePath) ||
                      directoryMappings.Any(m => m.SourceRelativePath != m.TargetRelativePath);
        var signature = Signature(input.Id, input.Fingerprint, policy, mappings, directoryMappings, output.Fingerprint);
        return new(input.Id, input.Fingerprint, policy, changed, mappings, directoryMappings,
            generated.OrderBy(p => p, StringComparer.Ordinal).Concat(singleRoot == null ? [] : new[] {singleRoot}).ToArray(), output, signature,
            generated.OrderBy(p => p, StringComparer.Ordinal).ToArray(), singleRoot);
    }

    internal static string RemoveAncestors(string path, IEnumerable<string> ancestors)
    {
        var prefixes = ancestors as IReadOnlySet<string> ?? ancestors.ToHashSet(StringComparer.Ordinal);
        var parts = path.Split('/');
        return string.Join('/', parts.Where((_, index) => !prefixes.Contains(string.Join('/', parts.Take(index + 1)))));
    }

    internal static bool Within(string path, string prefix, bool self = false) =>
        self && path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);

    internal static string Normalize(string path) => path.Replace('\\', '/');
    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') &&
        !path.Contains(':') && !path.Any(char.IsControl) && path.Split('/').All(p => p is not ("" or "." or ".."));

    private static void ValidatePaths(IEnumerable<string> files, IEnumerable<string> directories, bool portable = true)
    {
        var entries = files.ToArray(); var folders = directories.ToArray();
        string Key(string value) => portable ? value.Normalize(NormalizationForm.FormC).ToUpperInvariant() : value;
        if (entries.Concat(folders).Any(p => !Safe(p)) || folders.Select(Key).Distinct().Count() != folders.Length)
            throw new InvalidDataException("Delivery projection contains unsafe or colliding paths.");
        var nodes = new Dictionary<string, (string Path, bool File)>(StringComparer.Ordinal);
        foreach (var (path, directory) in entries.Select(p => (p, false)).Concat(folders.Select(p => (p, true))))
        {
            var parts = path.Split('/');
            for (var count = 1; count <= parts.Length; count++)
            {
                var candidate = string.Join('/', parts.Take(count)); var key = Key(candidate);
                var file = !directory && count == parts.Length;
                if (nodes.TryGetValue(key, out var prior) && (file || prior.File || prior.Path != candidate))
                    throw new InvalidDataException("Delivery projection conflicts with a file or directory.");
                nodes[key] = (candidate, file);
            }
        }
    }

    private static string Signature(string inputId, string inputFingerprint, ArtifactDeliveryPolicy? policy,
        IReadOnlyList<ArtifactDeliveryPathMapping> files, IReadOnlyList<ArtifactDeliveryPathMapping> directories, string outputFingerprint) =>
        ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {Version = 1, Id = inputId, Fingerprint = inputFingerprint,
            Policy = policy, FileMappings = files, DirectoryMappings = directories, OutputFingerprint = outputFingerprint}, Json));

    internal static bool ValidPublication(ArtifactDeliveryLayoutProof? proof, ArtifactManifest output)
    {
        if (proof is not {Version: 1, Policy: not null, FileMappings: not null, DirectoryMappings: not null} ||
            proof.Policy.Version != 1 || proof.InputFingerprint is not {Length: 64} ||
            proof.OutputFingerprint != output.Fingerprint || proof.OutputFingerprint != ArtifactManifestFiles.Fingerprint(output.Entries, output.Directories) ||
            proof.FileMappings.Any(m => m == null || !Safe(m.SourceRelativePath) || !Safe(m.TargetRelativePath)) ||
            proof.DirectoryMappings.Any(m => m == null || !Safe(m.SourceRelativePath) || m.TargetRelativePath != "" && !Safe(m.TargetRelativePath)) ||
            proof.FileMappings.Select(m => m.SourceRelativePath).Distinct(StringComparer.Ordinal).Count() != proof.FileMappings.Count ||
            proof.FileMappings.Select(m => m.TargetRelativePath).Distinct(StringComparer.Ordinal).Count() != proof.FileMappings.Count ||
            !proof.FileMappings.Select(m => m.TargetRelativePath).OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual(output.Entries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal))) return false;
        return proof.ProjectionFingerprint == Signature(proof.InputArtifactId, proof.InputFingerprint, proof.Policy,
            proof.FileMappings, proof.DirectoryMappings, proof.OutputFingerprint) &&
            proof.DirectoryMappings.Select(m => m.TargetRelativePath).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal).SequenceEqual((output.Directories ?? []).OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>Verify a publication against its immutable source receipt, even after source cleanup.</summary>
    internal static bool PreservesSource(ArtifactRecord source, ArtifactRecord output, ArtifactDeliveryLayoutProof? proof)
    {
        if (source.Fingerprint != source.Manifest.Fingerprint || output.Fingerprint != output.Manifest.Fingerprint ||
            !FileDispositionProofs.SameManifest(source.Manifest, source.Manifest) ||
            !FileDispositionProofs.SameManifest(output.Manifest, output.Manifest) ||
            source.Fingerprint != ArtifactManifestFiles.Fingerprint(source.Manifest.Entries, source.Manifest.Directories) ||
            output.Fingerprint != ArtifactManifestFiles.Fingerprint(output.Manifest.Entries, output.Manifest.Directories)) return false;
        // Older publications used identity paths and have no layout receipt.
        if (proof == null) return FileDispositionProofs.SameManifest(source.Manifest, output.Manifest);
        if (proof.InputArtifactId != source.Id || proof.InputFingerprint != source.Fingerprint ||
            !ValidPublication(proof, output.Manifest) ||
            !proof.FileMappings.Select(m => m.SourceRelativePath).OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual(source.Manifest.Entries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal)) ||
            !proof.DirectoryMappings.Select(m => m.SourceRelativePath).OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual((source.Manifest.Directories ?? []).OrderBy(p => p, StringComparer.Ordinal))) return false;
        var inputs = source.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var outputs = output.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        // A valid projection changes paths, never payloads. A matching projection signature
        // alone does not prove that every delivered byte is from the recorded source.
        return proof.FileMappings.All(m => FileDispositionProofs.SameContent(
            inputs[m.SourceRelativePath], outputs[m.TargetRelativePath]));
    }

    public static ArtifactDirectoryProvenance? ReadProvenance(ArtifactRecord artifact)
    {
        if (artifact.MetadataJson == null) return null;
        try
        {
            // Verified download metadata may already identify provider-created containers.
            // Carry that evidence into new processing checkpoints without changing the
            // download root, manifest or operation identity. Names alone remain insufficient.
            if (artifact.Kind is not ("download" or "processedFiles" or "publishedFiles" or "currentFiles")) return null;
            using var metadata = JsonDocument.Parse(artifact.MetadataJson);
            if (metadata.RootElement.ValueKind != JsonValueKind.Object ||
                !metadata.RootElement.TryGetProperty("directoryProvenance", out var member)) return null;
            var proof = member.Deserialize<ArtifactDirectoryProvenance>(Json);
            return Valid(proof, artifact.Manifest) ? proof : null;
        }
        catch (JsonException) { return null; }
    }

    internal static bool Valid(ArtifactDirectoryProvenance? evidence, ArtifactManifest manifest)
    {
        if (evidence is not {Version: 1, Entries: not null} || evidence.ManifestFingerprint != manifest.Fingerprint) return false;
        var files = manifest.Entries.Select(e => e.RelativePath).ToHashSet(StringComparer.Ordinal);
        var directories = (manifest.Directories ?? []).ToHashSet(StringComparer.Ordinal);
        return evidence.Entries.All(e => e != null && Safe(e.RelativePath) && e.GeneratedAncestors != null &&
            (e.IsDirectory ? directories : files).Contains(e.RelativePath) &&
            e.GeneratedAncestors.All(p => Safe(p) && Within(e.RelativePath, p, e.IsDirectory)) &&
            e.GeneratedAncestors.Distinct(StringComparer.Ordinal).Count() == e.GeneratedAncestors.Count) &&
        evidence.Entries.Select(e => (e.RelativePath, e.IsDirectory)).Distinct().Count() == evidence.Entries.Count;
    }

    internal static IReadOnlyDictionary<(string Path, bool Directory), IReadOnlyList<string>> Index(
        ArtifactDirectoryProvenance? evidence, ArtifactManifest manifest) => Valid(evidence, manifest)
        ? evidence!.Entries.ToDictionary(e => (e.RelativePath, e.IsDirectory), e => e.GeneratedAncestors) :
            new Dictionary<(string, bool), IReadOnlyList<string>>();

    internal static ArtifactDirectoryProvenance Bind(ArtifactManifest manifest, IEnumerable<ArtifactDirectoryEntry> entries)
    {
        var proof = new ArtifactDirectoryProvenance(1, manifest.Fingerprint, entries
            .Select(e => e with {RelativePath = Normalize(e.RelativePath), GeneratedAncestors = e.GeneratedAncestors.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray()})
            // An empty logical directory can have several contributing branches. Only
            // their shared evidence applies to the one merged manifest directory.
            .GroupBy(e => (e.RelativePath, e.IsDirectory)).Select(g => new ArtifactDirectoryEntry(g.Key.RelativePath,
                g.Key.IsDirectory, g.Select(e => e.GeneratedAncestors.AsEnumerable()).Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal)).ToArray()))
            .Where(e => e.GeneratedAncestors.Count > 0).ToArray());
        if (!Valid(proof, manifest)) throw new InvalidDataException("Measured generated-directory evidence differs from the result manifest.");
        return proof;
    }

    internal static ArtifactDirectoryProvenance? ProjectProvenance(ArtifactRecord input, ArtifactDeliveryLayoutPreview preview)
    {
        var evidence = ReadProvenance(input);
        if (evidence == null) return null;
        var maps = preview.FileMappings.Select(m => (Key: (m.SourceRelativePath, false), m.TargetRelativePath))
            .Concat(preview.DirectoryMappings.Select(m => (Key: (m.SourceRelativePath, true), m.TargetRelativePath)))
            .ToDictionary(x => x.Key, x => x.TargetRelativePath);
        var removed = (preview.RemovedGeneratedDirectories ?? []).ToHashSet(StringComparer.Ordinal);
        string Collapse(string path) => preview.CollapsedAuthorRootDirectory is not { } root ? path :
            path == root ? "" : Within(path, root) ? path[(root.Length + 1)..] : path;
        return Bind(preview.OutputManifest, evidence.Entries.Select(e => new ArtifactDirectoryEntry(
                maps[(e.RelativePath, e.IsDirectory)], e.IsDirectory,
                e.GeneratedAncestors.Where(p => !removed.Contains(p)).Select(p => Collapse(RemoveAncestors(p, removed)))
                    .Where(p => p.Length > 0).ToArray()))
            .Where(e => e.RelativePath.Length > 0));
    }

    internal static IReadOnlyList<string> RebaseAncestors(IReadOnlyList<string>? ancestors, string source, string target)
    {
        source = Normalize(source); target = Normalize(target == "." ? "" : target);
        return (ancestors ?? []).Where(p => Within(p, source)).Select(p =>
            target.Length == 0 ? p[(source.Length + 1)..] : target + p[source.Length..]).ToArray();
    }
}
