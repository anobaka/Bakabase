using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>One immutable path/relocation snapshot per reference check. Allocation roots and
/// actual references still have different ancestor semantics, decided by the caller.</summary>
internal sealed class ArtifactCleanupPathReferences
{
    private sealed record Alias(string Source, string Destination);
    private readonly Alias[] aliases;
    private readonly Alias[] reverseAliases;
    private readonly string[] roots;
    private readonly HashSet<string> rootSet;
    private readonly string[] sortedRoots;
    private readonly CancellationToken cancellationToken;
    private readonly Dictionary<string, string> normalized = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> relocated = new(ArtifactManifestFiles.PathComparer);
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Internal work counts make large reference-set regressions deterministic, without a
    // timing threshold or a production logging/API surface.
    internal long PathComparisonCount { get; private set; }
    internal int NormalizedPathCount => normalized.Count;

    internal ArtifactCleanupPathReferences(IEnumerable<string> candidateRoots,
        IReadOnlyList<FileProcessingWorkspaceResolver.Relocation> mappings, CancellationToken ct)
    {
        cancellationToken = ct;
        ct.ThrowIfCancellationRequested();
        aliases = mappings.Select(a => new Alias(Normalize(a.SourceRoot), Normalize(a.DestinationRoot))).ToArray();
        roots = candidateRoots.Select(Relocate).Distinct(ArtifactManifestFiles.PathComparer).ToArray();
        rootSet = roots.ToHashSet(ArtifactManifestFiles.PathComparer);
        sortedRoots = roots.OrderBy(p => p, ArtifactManifestFiles.PathComparer).ToArray();
        // The reverse check can only match a mapping whose destination overlaps a candidate.
        // Compute that once, not for every reference × every private file × every mapping.
        reverseAliases = aliases.Where(a => RefersCandidate(a.Destination, ancestors: true)).ToArray();
    }

    internal IReadOnlyList<string> Roots => roots;

    internal string Relocate(string path)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Normalize(path);
        if (relocated.TryGetValue(full, out var cached)) return cached;
        Alias? match = null;
        foreach (var alias in aliases)
        {
            if (!Under(full, alias.Source)) continue;
            // Preserve SingleOrDefault's ambiguity refusal; never choose a longest/first
            // mapping and silently reinterpret overlapping or duplicate alias roots.
            if (match != null) throw new InvalidOperationException("More than one relocation matches this reference path.");
            match = alias;
        }
        return relocated[full] = match == null ? full : Normalize(Path.Combine(match.Destination,
            Path.GetRelativePath(match.Source, full)));
    }

    internal bool Refers(string value, bool ancestors = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(value)) return false;
        var original = Normalize(value);
        var resolved = Relocate(value);
        // An old parent directory can contain a mapped input without itself being beneath
        // a mapping. This fact is independent of which private file root is being tested.
        if (ancestors && reverseAliases.Any(a => Under(a.Source, original))) return true;
        return RefersCandidate(resolved, ancestors);
    }

    private bool RefersCandidate(string resolved, bool ancestors)
    {
        // A file can have only as many candidate ancestors as path components. Testing
        // those keys avoids scanning every private file for every historical reference.
        CountComparison();
        if (rootSet.Contains(resolved)) return true;
        for (var end = resolved.LastIndexOf(Path.DirectorySeparatorChar); end > 0;
             end = resolved.LastIndexOf(Path.DirectorySeparatorChar, end - 1))
        {
            CountComparison();
            if (rootSet.Contains(resolved[..end])) return true;
        }
        if (!ancestors) return false;

        // All descendants occupy one sorted prefix range. The separator is essential:
        // an allocation named "task" must never claim its sibling "task-extra".
        var prefix = resolved + Path.DirectorySeparatorChar;
        var low = 0;
        var high = sortedRoots.Length;
        while (low < high)
        {
            CountComparison();
            var middle = low + (high - low) / 2;
            if (ArtifactManifestFiles.PathComparer.Compare(sortedRoots[middle], prefix) < 0)
                low = middle + 1;
            else high = middle;
        }
        return low < sortedRoots.Length && Under(sortedRoots[low], resolved);
    }

    private string Normalize(string value)
    {
        if (normalized.TryGetValue(value, out var path)) return path;
        return normalized[value] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    internal static bool StrictlyUnder(string file, string parent) => file.Length > parent.Length &&
        file[parent.Length] == Path.DirectorySeparatorChar && file.StartsWith(parent, Comparison);

    private bool Under(string file, string parent)
    {
        CountComparison();
        return ArtifactManifestFiles.PathComparer.Equals(file, parent) ||
               StrictlyUnder(file, parent);
    }

    private void CountComparison()
    {
        if ((++PathComparisonCount & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
    }
}
