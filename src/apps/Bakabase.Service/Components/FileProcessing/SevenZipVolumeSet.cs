using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using SharpCompress.Compressors.Deflate;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>An explicit extraction step can recover a masked first volume using only its registered inputs.</summary>
internal sealed partial record SevenZipVolumeSet(string FamilyPath, string[] Sources)
{
    private static readonly Regex Part = new(@"^(?<family>.+)\.(?<n>[0-9]{3})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private const int MaxHeaderBytes = 1024 * 1024;
    public string DirectoryName => Path.GetFileName(FamilyPath).EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(FamilyPath) : Path.GetFileName(FamilyPath);


    internal static async Task<List<SevenZipVolumeSet>> FindAsync(IReadOnlyList<string> paths, CancellationToken ct, bool includeNamedFirst = false)
    {
        if (paths.Count is 0 or > 1024) return [];
        var original = await FindUnwrappedAsync(paths, ct, includeNamedFirst);
        var wrapped = await FindWrappedVolumesAsync(paths, original, ct);
        original.AddRange(wrapped);
        return original;
    }

    private static async Task<List<SevenZipVolumeSet>> FindUnwrappedAsync(IReadOnlyList<string> paths, CancellationToken ct, bool includeNamedFirst)
    {
        var found = new List<SevenZipVolumeSet>();
        if (paths.Count < 2) return found;
        var numbered = paths.Select(path => (Path: path, Match: Part.Match(path)))
            .Where(item => item.Match.Success).ToArray();
        var families = numbered.Where(item => !includeNamedFirst || item.Match.Groups["n"].Value != "001")
            .GroupBy(item => item.Match.Groups["family"].Value, FileProcessingFiles.PathComparer)
            .Select(group => (group.Key, Parts: group.OrderBy(item => item.Match.Groups["n"].Value, StringComparer.Ordinal).ToArray()))
            .Where(group => group.Parts.Select(item => int.Parse(item.Match.Groups["n"].Value, CultureInfo.InvariantCulture))
                .SequenceEqual(Enumerable.Range(2, group.Parts.Length))).ToArray();
        if (families.Length is 0 or > 8) return found;
        var numberedPaths = numbered.Select(item => item.Path).ToHashSet(FileProcessingFiles.PathComparer);
        var candidates = paths.Where(path => !numberedPaths.Contains(path) || includeNamedFirst && path.EndsWith(".001", StringComparison.Ordinal)).ToArray();
        if (candidates.Length > 32) return found;
        var firsts = new List<(string Path, long Length, long Extent, int HeaderLength, uint HeaderCrc)>();
        foreach (var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var length = new FileInfo(path).Length;
            if (length < 32) continue;
            var bytes = new byte[32];
            await using var stream = Open(path);
            await stream.ReadExactlyAsync(bytes, ct);
            var extent = DeclaredExtent(bytes);
            var headerLength = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20));
            if (extent > length && headerLength <= MaxHeaderBytes)
                firsts.Add((path, length, extent.Value, (int)headerLength, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28))));
        }
        foreach (var (family, members) in families)
        {
            var tails = members.Select(item => item.Path).ToArray();
            var lengths = tails.Select(path => new FileInfo(path).Length).ToArray();
            if (lengths.Any(length => length <= 0)) continue;
            var tailBytes = lengths.Sum();
            var plausible = firsts.Where(first => (!numberedPaths.Contains(first.Path) || FileProcessingFiles.PathComparer.Equals(first.Path, family + ".001")) &&
                first.Extent - first.Length == tailBytes &&
                lengths[^1] <= first.Length && lengths.SkipLast(1).All(length => length == first.Length)).ToList();
            // More candidates could exceed the bounded metadata budget. Never choose by directory or listing order.
            if (plausible.Count > 4) return [];
            foreach (var first in plausible)
            {
                var sources = tails.Prepend(first.Path).ToArray();
                if (await EndHeaderMatchesAsync(sources, first.HeaderLength, first.HeaderCrc, ct))
                    found.Add(new(family, sources));
            }
        }
        // A first volume can match two sets with identical header metadata; neither binding is safe in that case.
        return found.Where(set => found.Count(other => FileProcessingFiles.PathComparer.Equals(other.FamilyPath, set.FamilyPath) ||
            FileProcessingFiles.PathComparer.Equals(other.Sources[0], set.Sources[0])) == 1).ToList();
    }

    // https://github.com/ip7z/7zip/blob/main/DOC/7zFormat.txt (SignatureHeader), reviewed 2026-11-12.
    // CRC proves this small structural header only. The selected extractor still verifies the
    // complete volume set, payload and password after any explicitly proposed rename.
    internal static long? DeclaredExtent(byte[] header)
    {
        if (header.Length != 32 || ArchiveSignature.Detect(header) != ".7z" || header[6] != 0) return null;
        var crc = new CRC32(); crc.SlurpBlock(header, 12, 20);
        if ((uint)crc.Crc32Result != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8))) return null;
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(12));
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(20));
        if (size == 0 || offset > (ulong)long.MaxValue - 32UL || size > (ulong)long.MaxValue - 32UL - offset) return null;
        return checked((long)(32UL + offset + size));
    }

    // Earlier versions passed a verified private link to automatic structural discovery.
    // A retry still requires that the registered ordinary source files prove one complete volume set.
    private const string HistoricalLinkFailure = "Archive structure discovery requires a regular file.";
    internal static bool IsRecoverableFailure(string? error) => error == HistoricalLinkFailure || error?.StartsWith("The archive volume set for ", StringComparison.Ordinal) == true &&
        error.EndsWith(" has missing parts.", StringComparison.Ordinal);

    // Historical failures predate structured extraction waits. Only the first explicit, unrestricted extraction
    // can be recovered without interpreting an unexecuted rename, branching selector, or earlier output.
    internal static async Task<bool> CanRecoverAsync(string? error, FileProcessingPlan plan, string? boundary,
        IReadOnlyList<string> files, CancellationToken ct, (FileProcessingPlanExecutor.State State, string Root)? checkpoint = null)
    {
        if (!IsRecoverableFailure(error) || plan.Steps.FirstOrDefault() is not { Op: "extractArchive" } step ||
            boundary != null && boundary != step.Id || !step.GetInputReferences().SequenceEqual(["download"]) ||
            !string.IsNullOrEmpty(step.Selector) || step.ArchiveOpenMode == ArchiveOpenMode.SevenZipParser)
            return false;
        var sets = await FindAsync(files, ct);
        if (error == HistoricalLinkFailure ? sets.Count != 1 :
            !sets.Any(set => error == $"The archive volume set for {Path.GetFileName(set.Sources[1])} has missing parts.")) return false;
        if (checkpoint is { } saved && saved.State.Operations.Count > 0)
        {
            // The old tool rejected the private link after binding group zero, before opening its output.
            if (error != HistoricalLinkFailure || saved.State.Operations.Count != 1 ||
                !saved.State.Operations.TryGetValue(FileProcessingPlanExecutor.StepKey(step.Id) + "-0", out var operation) ||
                operation is not {Completed: false, OutputManifest: null, ExtractionStagingId: null,
                    RenameFiles: null, ProjectedDirectories: null, OutputDirectory: { } output, ArchiveInputs: { } binding} ||
                !binding.SequenceEqual(sets.Single().Sources.Select(path => ArtifactManifestFiles.RelativeFile(saved.Root, path)),
                    FileProcessingFiles.PathComparer)) return false;
            var destination = FileProcessingFiles.Within(output, Path.Combine(saved.Root, "outputs"));
            if (!ArtifactCleanupAbsence.AllConfirmedAbsent(saved.Root, [destination, destination + ".pending"], ct)) return false;
        }
        var repaired = sets.SelectMany(set => set.Sources).ToHashSet(FileProcessingFiles.PathComparer);
        try {FileProcessingFiles.ValidateVolumes(files.Where(path => !repaired.Contains(path)), inspectSiblingDownloads: false);}
        catch (InvalidOperationException) {return false;}
        return true;
    }

    private static FileStream Open(string path)
    {
        ArtifactManifestFiles.RejectLinks(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
    }

    private static async Task<bool> EndHeaderMatchesAsync(string[] sources, int length, uint expected, CancellationToken ct)
    {
        var bytes = new byte[length];
        var remaining = length;
        for (var i = sources.Length - 1; remaining > 0 && i >= 0; i--)
        {
            await using var stream = Open(sources[i]);
            var count = (int)Math.Min(remaining, stream.Length);
            stream.Seek(-count, SeekOrigin.End);
            remaining -= count;
            await stream.ReadExactlyAsync(bytes.AsMemory(remaining, count), ct);
        }
        var crc = new CRC32();
        crc.SlurpBlock(bytes, 0, bytes.Length);
        return remaining == 0 && (uint)crc.Crc32Result == expected;
    }

    // This is only an archive-tool input view. The immutable checkpoint, operation identities, hashes and
    // replacement receipts continue to refer to the original authorized paths. A crash leaves only tiny links.
    internal sealed class ToolInputs : IDisposable
    {
        private readonly DirectoryInfo directory;
        private readonly string[] sources;
        public string[] Files { get; }

        public ToolInputs(SevenZipVolumeSet set) : this(set.Sources, standaloneZip: false) { }

        // Give the tool only this immutable file. Adjacent, unregistered volumes are invisible.
        public ToolInputs(string standaloneZip) : this([standaloneZip], standaloneZip: true) { }

        private ToolInputs(string[] paths, bool standaloneZip)
        {
            directory = Directory.CreateTempSubdirectory("bakabase-archive-");
            sources = paths;
            Files = sources.Select((_, i) => Path.Combine(directory.FullName,
                standaloneZip ? "archive.zip" : "archive.7z." + (i + 1).ToString("D3", CultureInfo.InvariantCulture))).ToArray();
            try
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    ArtifactManifestFiles.RejectLinks(sources[i]);
                    File.CreateSymbolicLink(Files[i], Path.GetFullPath(sources[i]));
                }
                Validate();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Dispose();
                throw new ArchiveFormatProbeIncompleteException("A private volume alias could not be prepared on this system. Use a processing plan to name and place the selected volumes.", "ArchiveInputRequired");
            }
        }

        public void Validate()
        {
            if (directory.LinkTarget != null || !Directory.EnumerateFileSystemEntries(directory.FullName)
                    .ToHashSet(FileProcessingFiles.PathComparer).SetEquals(Files))
                throw Changed();
            for (var i = 0; i < Files.Length; i++)
            {
                ArtifactManifestFiles.RejectLinks(sources[i]);
                if (!FileProcessingFiles.PathComparer.Equals(new FileInfo(Files[i]).LinkTarget, Path.GetFullPath(sources[i])))
                    throw Changed();
            }
        }

        private static ArchiveFormatProbeIncompleteException Changed() => new(
            "The private archive volume names no longer match the registered files.", "ArchiveGroupInputChanged");

        public void Dispose()
        {
            // Remove our exact links. Unexpected files are never followed or recursively removed.
            if (new DirectoryInfo(directory.FullName).LinkTarget != null) return;
            for (var i = 0; i < Files.Length; i++)
                if (FileProcessingFiles.PathComparer.Equals(new FileInfo(Files[i]).LinkTarget, Path.GetFullPath(sources[i])))
                    File.Delete(Files[i]);
            if (!Directory.EnumerateFileSystemEntries(directory.FullName).Any()) Directory.Delete(directory.FullName);
        }
    }
}
