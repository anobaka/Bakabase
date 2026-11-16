using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    // Selection only: these are filename hints, never evidence that extraction is safe.
    // Work entirely from the registered manifest; do not probe every game/media file.
    private static ArtifactManifestEntry[] OrderPartialManifest(ArtifactManifest manifest)
    {
        var entries = manifest.Entries;
        var archives = CompressedFileHelper.DetectCompressedFileGroups(
                entries.Select(entry => entry.RelativePath).ToArray(), inspectFileSizes: false)
            .SelectMany(group => group.Files).ToHashSet(StringComparer.Ordinal);

        // Ignore an enclosing author/system directory when deciding which independent
        // branches deserve a turn. A root-level file is its own root-files branch.
        var common = entries.Count == 0 ? Array.Empty<string>() : ParentSegments(entries[0].RelativePath);
        foreach (var entry in entries.Skip(1))
        {
            var parent = ParentSegments(entry.RelativePath);
            common = common.Take(common.Zip(parent).TakeWhile(pair => pair.First == pair.Second).Count()).ToArray();
            if (common.Length == 0) break;
        }
        var prefixLength = common.Length == 0 ? 0 : string.Join('/', common).Length + 1;
        var branches = entries.Select((entry, index) =>
            {
                var relative = entry.RelativePath[prefixLength..];
                var split = relative.IndexOf('/');
                return new {entry, index, branch = split < 0 ? "" : relative[..split],
                    archive = archives.Contains(entry.RelativePath), depth = relative.Count(c => c == '/')};
            })
            .GroupBy(item => item.branch, StringComparer.Ordinal)
            .OrderByDescending(group => group.Any(item => item.archive))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Queue<ArtifactManifestEntry>(group
                .OrderByDescending(item => item.archive).ThenBy(item => item.depth).ThenBy(item => item.index)
                .Select(item => item.entry))).ToArray();

        // Round-robin survives later byte-budget reductions: taking a shorter prefix
        // still shows each branch before spending another slot on a large sibling.
        var result = new List<ArtifactManifestEntry>(entries.Count);
        var active = new Queue<Queue<ArtifactManifestEntry>>(branches);
        while (active.TryDequeue(out var branch))
        {
            result.Add(branch.Dequeue());
            if (branch.Count > 0) active.Enqueue(branch);
        }
        return result.ToArray();
    }

    private static string[] ParentSegments(string path)
    {
        var split = path.LastIndexOf('/');
        return split < 0 ? [] : path[..split].Split('/');
    }
}
