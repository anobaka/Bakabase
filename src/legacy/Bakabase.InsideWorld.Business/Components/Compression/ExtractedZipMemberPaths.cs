using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Some Unix extractors materialize DOS ZIP separators as literal filename characters.
/// Only repairs an exclusively owned, unpublished extraction with a committed content receipt.</summary>
public static class ExtractedZipMemberPaths
{
    public static async Task<bool> TryRestoreAsync(string root, IReadOnlyCollection<string> physicalFiles,
        ArtifactManifest expected, IReadOnlyList<string> volumes, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows() || volumes.Count != 1 || expected.Entries.Count != physicalFiles.Count ||
            !physicalFiles.Any(path => Path.GetRelativePath(root, path).Contains('\\')) ||
            expected.Fingerprint != ArtifactManifestFiles.Fingerprint(expected.Entries, expected.Directories))
            return false;
        var zip = volumes[0];
        ArtifactManifestFiles.RejectLinks(zip);
        var structure = await BoundedZipStructureProbe.FindStructureAsync(zip, new FileInfo(zip).Length, ct,
            includeStandaloneZip: true);
        if (structure is not {ZipOffset: 0, WindowsMembers: true}) return false;
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count != structure.EntryCount) return false;
        var members = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = member.FullName;
            if (name.EndsWith('/') || name.EndsWith('\\')) continue;
            var relative = ExplicitZipNameReader.SafeName(name, false);
            if ((member.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                ((member.ExternalAttributes >> 16) & 0xf000) is not (0 or 0x8000) ||
                !members.TryAdd(relative.Normalize(), member)) return false;
        }
        if (members.Count != physicalFiles.Count) return false;
        _ = ArtifactManifestFiles.ResolveFiles(root, expected);
        var recorded = expected.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moves = new List<(string From, string To)>();
        foreach (var file in physicalFiles)
        {
            var physical = Path.GetRelativePath(root, file);
            var relative = ExplicitZipNameReader.SafeName(physical, false);
            if (!observed.Add(relative) || !recorded.TryGetValue(relative, out var entry) ||
                !members.TryGetValue(relative.Normalize(), out var member) || entry.Length != member.Length ||
                member.FullName.Replace('\\', '/') != relative || physical != relative && physical != member.FullName)
                return false;
            var destination = Path.Combine(root, relative);
            ArtifactManifestFiles.RejectLinks(destination);
            for (var part = relative; part != null; part = Path.GetDirectoryName(part))
            {
                if (part.Length == 0) break;
                var key = part.Normalize();
                if (spellings.TryGetValue(key, out var spelling) && spelling != part) return false;
                spellings[key] = part;
                if (part != relative && (File.Exists(Path.Combine(root, part)) || members.ContainsKey(key))) return false;
            }
            if (physical == relative) continue;
            if (File.Exists(destination) || Directory.Exists(destination)) return false;
            moves.Add((file, destination));
        }
        if (moves.Count == 0) return false;
        // Verify every physical byte against the existing receipt before moving any file.
        var captured = await ArtifactManifestFiles.CaptureAsync(root, physicalFiles, ct);
        if (captured.Entries.Any(entry => !recorded.TryGetValue(entry.RelativePath, out var prior) ||
                entry.Length != prior.Length || entry.Sha256 != prior.Sha256)) return false;
        foreach (var (from, to) in moves)
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(from);
            ArtifactManifestFiles.RejectLinks(to);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            ArtifactManifestFiles.RejectLinks(to);
            File.Move(from, to, overwrite: false);
        }
        return true;
    }
}
