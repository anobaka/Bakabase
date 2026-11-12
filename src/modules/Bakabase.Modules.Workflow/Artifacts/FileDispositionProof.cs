namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>Produced by a successful executor, never by an analysis plan or an LLM response.</summary>
public sealed record FileLineageEntry(string InputRelativePath, IReadOnlyList<string> OutputRelativePaths,
    bool Replaced = false);

/// <summary>Hashes describe the files actually read and produced by this execution.</summary>
public sealed record FileLineage(IReadOnlyList<FileLineageEntry> Entries,
    ArtifactManifest? InputManifest = null, ArtifactManifest? OutputManifest = null);

public sealed record FileDispositionEntry(ArtifactManifestEntry Source, string Disposition,
    IReadOnlyList<ArtifactManifestEntry> Outputs);

/// <summary>Manifest-bound per-file dispositions. Unknown entries never authorize retirement.</summary>
public sealed record FileDispositionProof(int Version, string InputArtifactId, string InputFingerprint,
    string OutputFingerprint, IReadOnlyList<FileDispositionEntry> Entries);

public static class FileDispositionProofs
{
    public const int Version = 1;

    public static bool SameContent(ArtifactManifestEntry a, ArtifactManifestEntry b) =>
        a.Length == b.Length && string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase);

    public static bool SameEntry(ArtifactManifestEntry a, ArtifactManifestEntry b) =>
        a.RelativePath == b.RelativePath && SameContent(a, b);

    public static bool SameManifest(ArtifactManifest? a, ArtifactManifest? b)
    {
        if (a?.Entries == null || b?.Entries == null || a.Fingerprint != b.Fingerprint ||
            a.Entries.Count != b.Entries.Count || !ValidEntries(a.Entries) || !ValidEntries(b.Entries) ||
            !(a.Directories ?? []).OrderBy(path => path, StringComparer.Ordinal)
                .SequenceEqual((b.Directories ?? []).OrderBy(path => path, StringComparer.Ordinal))) return false;
        var entries = b.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        return a.Entries.All(e => entries.TryGetValue(e.RelativePath, out var other) && SameEntry(e, other));
    }

    /// <summary>Bind only the executor's measured manifests; a declared plan cannot create a proof.</summary>
    public static FileDispositionProof Create(ArtifactRecord input, ArtifactManifest output, FileLineage? lineage)
    {
        var measured = lineage != null && SameManifest(input.Manifest, lineage.InputManifest) &&
                       SameManifest(output, lineage.OutputManifest) && ValidLineage(lineage);
        var mappings = measured ? lineage!.Entries.ToDictionary(e => Normalize(e.InputRelativePath), StringComparer.Ordinal) : [];
        var outputs = output.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var entries = new List<FileDispositionEntry>();
        foreach (var source in input.Manifest.Entries)
        {
            var disposition = "unknown";
            IReadOnlyList<ArtifactManifestEntry> destinations = [];
            if (mappings.TryGetValue(source.RelativePath, out var mapping) && mapping.OutputRelativePaths.Count > 0 &&
                mapping.OutputRelativePaths.All(p => outputs.ContainsKey(Normalize(p))))
            {
                destinations = mapping.OutputRelativePaths.Select(p => outputs[Normalize(p)]).ToArray();
                // A retained file must preserve all mapped contents. Replacement is an explicit
                // successful executor operation, not a claim inferred from a filename or a plan.
                if (destinations.All(d => SameContent(source, d))) disposition = "preserved";
                else if (mapping.Replaced) disposition = "replaced";
            }
            entries.Add(new(source, disposition, destinations));
        }
        return new(Version, input.Id, input.Fingerprint, output.Fingerprint, entries);
    }

    /// <summary>Publication preserves relative paths and verified bytes, including unknown siblings.</summary>
    public static FileDispositionProof Preserve(ArtifactRecord input, ArtifactManifest output) => Create(input,
        output, new(input.Manifest.Entries.Select(e => new FileLineageEntry(e.RelativePath, [e.RelativePath])).ToArray(),
            input.Manifest, output));

    public static bool ValidFor(FileDispositionProof? proof, ArtifactRecord input, ArtifactRecord output)
    {
        if (proof is not {Version: Version} || proof.InputArtifactId != input.Id ||
            proof.InputFingerprint != input.Fingerprint || proof.OutputFingerprint != output.Fingerprint ||
            input.Fingerprint != input.Manifest.Fingerprint || output.Fingerprint != output.Manifest.Fingerprint ||
            proof.Entries == null || proof.Entries.Count != input.Manifest.Entries.Count ||
            !ValidEntries(input.Manifest.Entries) || !ValidEntries(output.Manifest.Entries) ||
            proof.Entries.Any(e => e?.Source == null || e.Outputs == null ||
                e.Disposition is not ("preserved" or "replaced" or "unknown")) ||
            proof.Entries.Select(e => e.Source.RelativePath).Distinct(StringComparer.Ordinal).Count() != proof.Entries.Count)
            return false;
        var inputs = input.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var outputs = output.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        return proof.Entries.All(e => inputs.TryGetValue(e.Source.RelativePath, out var source) && SameEntry(source, e.Source) &&
            e.Outputs.All(d => d != null && outputs.TryGetValue(d.RelativePath, out var target) && SameEntry(d, target)) &&
            e.Outputs.Select(d => d.RelativePath).Distinct(StringComparer.Ordinal).Count() == e.Outputs.Count &&
            (e.Disposition == "unknown" || e.Outputs.Count > 0) &&
            (e.Disposition != "preserved" || e.Outputs.All(d => SameContent(e.Source, d))));
    }

    /// <summary>Compose dynamic/recursive execution without losing any branch of a replacement.</summary>
    public static FileLineage? Compose(FileLineage? first, FileLineage? second)
    {
        if (first == null || second == null || !SameManifest(first.OutputManifest, second.InputManifest) ||
            !ValidLineage(first) || !ValidLineage(second)) return null;
        var next = second.Entries.ToDictionary(e => Normalize(e.InputRelativePath), StringComparer.Ordinal);
        var entries = new List<FileLineageEntry>();
        foreach (var entry in first.Entries)
        {
            if (entry.OutputRelativePaths.Count == 0 || entry.OutputRelativePaths.Any(p =>
                    !next.TryGetValue(Normalize(p), out var child) || child.OutputRelativePaths.Count == 0))
            {
                entries.Add(new(entry.InputRelativePath, []));
                continue;
            }
            var children = entry.OutputRelativePaths.Select(p => next[Normalize(p)]).ToArray();
            entries.Add(new(entry.InputRelativePath, children.SelectMany(e => e.OutputRelativePaths)
                    .Select(Normalize).Distinct(StringComparer.Ordinal).ToArray(),
                entry.Replaced || children.Any(e => e.Replaced)));
        }
        return new(entries, first.InputManifest, second.OutputManifest);
    }

    private static bool ValidLineage(FileLineage value) => value.Entries != null &&
        value.Entries.All(e => e != null && Safe(Normalize(e.InputRelativePath)) && e.OutputRelativePaths != null &&
            e.OutputRelativePaths.All(p => Safe(Normalize(p))) &&
            e.OutputRelativePaths.Select(Normalize).Distinct(StringComparer.Ordinal).Count() == e.OutputRelativePaths.Count) &&
        value.Entries.Select(e => Normalize(e.InputRelativePath)).Distinct(StringComparer.Ordinal).Count() == value.Entries.Count;

    private static bool ValidEntries(IReadOnlyList<ArtifactManifestEntry> entries) => entries.All(e =>
        e != null && Safe(e.RelativePath) && e.Length >= 0 && e.Sha256 is {Length: 64} &&
        e.Sha256.All(Uri.IsHexDigit)) && entries.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count() == entries.Count;

    private static string Normalize(string? path) => path?.Replace('\\', '/') ?? "";
    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') &&
        !path.Contains(':') && !path.Contains('\0') && path.Split('/').All(p => p is not ("" or "." or ".."));
}
