using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed partial record SevenZipVolumeSet
{
    // A collision suffix can occur immediately before the publisher's wrapper extension.
    // The candidate name never grants membership: the registered files must also prove the
    // complete contiguous extent and both 7-Zip metadata checksums.
    private static readonly Regex WrappedPart = new(
        @"^(?<stem>.+)\.(?<ordinal>[0-9]{3})(?:~[0-9a-fA-F]{16})?(?<wrapper>\.[\p{L}][\p{L}\p{N}_-]{0,15})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static bool MayContainVolumes(string path) =>
        path.EndsWith(".002", StringComparison.Ordinal) || WrappedPart.Match(Path.GetFileName(path)) is {Success: true} match && match.Groups["ordinal"].Value == "001";

    private static async Task<List<SevenZipVolumeSet>> FindWrappedVolumesAsync(IReadOnlyList<string> paths, IReadOnlyList<SevenZipVolumeSet> existing, CancellationToken ct)
    {
        var found = new List<SevenZipVolumeSet>();
        var groups = paths.Select(path => (Path: path, Match: WrappedPart.Match(Path.GetFileName(path))))
            .Where(x => x.Match.Success)
            .GroupBy(x => Path.Combine(Path.GetDirectoryName(x.Path) ?? "", x.Match.Groups["stem"].Value), FileProcessingFiles.PathComparer);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var members = group.Select(x => (x.Path, Ordinal: int.Parse(x.Match.Groups["ordinal"].Value, CultureInfo.InvariantCulture),
                    Wrapper: x.Match.Groups["wrapper"].Value)).OrderBy(x => x.Ordinal).ToArray();
            var firsts = members.Where(x => x.Ordinal == 1).ToArray();
            if (firsts.Length == 0) continue;
            foreach (var first in firsts)
            {
                await using var stream = Open(first.Path);
                if (stream.Length < 32) continue;
                var header = new byte[32];
                await stream.ReadExactlyAsync(header, ct);
                if (ArchiveSignature.Detect(header) != ".7z") continue;
                var extent = DeclaredExtent(header);
                if (members.Length == 1 && existing.Any(set => FileProcessingFiles.PathComparer.Equals(set.Sources[0], first.Path))) continue;
                if (extent == stream.Length) continue; // A complete standalone file does not consume its neighbours.
                if (found.Count >= 8 || extent == null || extent < stream.Length || firsts.Length != 1 ||
                    existing.Any(set => set.Sources.Intersect(members.Select(x => x.Path), FileProcessingFiles.PathComparer).Any()) ||
                    members.Select(x => x.Wrapper).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
                    members.Select(x => x.Ordinal).Distinct().Count() != members.Length ||
                    paths.Any(p => Part.Match(p) is {Success: true} m && FileProcessingFiles.PathComparer.Equals(m.Groups["family"].Value, group.Key)))
                    throw new ArchiveFormatProbeIncompleteException("The selected archive has conflicting volume names or header evidence.", "ArchiveInputRequired");
                var sources = members.Select(x => x.Path).ToArray();
                var lengths = sources.Select(p => new FileInfo(p).Length).ToArray();
                if (sources.Length < 2 || !members.Select(x => x.Ordinal).SequenceEqual(Enumerable.Range(1, members.Length)) ||
                    lengths[^1] <= 0 || lengths[^1] > stream.Length || lengths.SkipLast(1).Any(n => n != stream.Length) ||
                    lengths.Sum() != extent.Value)
                    throw new ArchiveFormatProbeIncompleteException("The selected archive volume set is incomplete.", "MissingVolumes");
                var tailLength = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(20));
                if (tailLength > MaxHeaderBytes || !await EndHeaderMatchesAsync(sources, (int)tailLength,
                        BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28)), ct))
                    throw new ArchiveFormatProbeIncompleteException("The registered volumes do not match the archive's declared end header.", "ArchiveInputRequired");
                found.Add(new(group.Key, sources));
            }
        }
        return found;
    }
}
