using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>
/// Handles DLsite archive extraction with support for:
/// - Split archives where the first part is a self-extracting .exe
/// - ZIP archives with Japanese encoding detection
/// - RAR and other formats via 7-Zip
/// </summary>
public class DLsiteArchiveExtractor(
    SevenZipService sevenZipService,
    ILogger<DLsiteArchiveExtractor> logger)
{
    private enum ArchiveFormat
    {
        Unknown,
        Zip,
        Rar
    }

    // ZIP local file header: PK\x03\x04
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    // RAR5: Rar!\x1A\x07\x01\x00
    private static readonly byte[] Rar5Magic = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    // RAR4: Rar!\x1A\x07\x00
    private static readonly byte[] Rar4Magic = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];

    /// <summary>
    /// Extracts DLsite downloaded files. Handles split archives (with .exe first part)
    /// and selects the appropriate extraction method based on archive format.
    /// </summary>
    /// <param name="downloadedFiles">List of downloaded file paths, in order.</param>
    /// <param name="outputDir">Directory to extract into.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if any archives were extracted.</returns>
    public async Task<bool> ExtractAsync(List<string> downloadedFiles, string outputDir, CancellationToken ct)
    {
        if (downloadedFiles.Count == 0) return false;

        // Group files into archive sets (split archives belong together)
        var archiveSets = GroupIntoArchiveSets(downloadedFiles);

        var extracted = false;
        foreach (var set in archiveSets)
        {
            ct.ThrowIfCancellationRequested();

            var entryFile = set.EntryFile;
            var format = set.Format;

            if (format == ArchiveFormat.Unknown)
            {
                logger.LogDebug("Skipping non-archive file: {Path}", entryFile);
                continue;
            }

            // If the entry file is .exe (SFX), rename it to the correct extension
            // so that extraction tools can recognize the split archive properly.
            string? renamedEntry = null;
            if (entryFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var targetExt = format == ArchiveFormat.Zip ? ".zip" : ".rar";
                renamedEntry = Path.ChangeExtension(entryFile, targetExt);
                File.Move(entryFile, renamedEntry);
                logger.LogInformation("Renamed SFX entry {From} -> {To}", entryFile, renamedEntry);
                entryFile = renamedEntry;
            }

            try
            {
                Directory.CreateDirectory(outputDir);

                if (format == ArchiveFormat.Zip && set.Files.Count == 1)
                {
                    // Single ZIP file: use .NET ZipFile with encoding detection
                    ExtractZipWithEncodingDetection(entryFile, outputDir);
                }
                else
                {
                    // Split archives (any format) or non-ZIP: use 7-Zip
                    // 7-Zip automatically finds sibling split parts when given the first volume
                    await sevenZipService.Extract(entryFile, outputDir, ct);
                }

                extracted = true;
                logger.LogInformation("Extracted archive: {Path} (format={Format}, parts={Count})",
                    entryFile, format, set.Files.Count);
            }
            catch (Exception ex)
            {
                // If we renamed the file, restore the original name before re-throwing
                if (renamedEntry != null && File.Exists(renamedEntry))
                {
                    var originalPath = set.OriginalEntryFile;
                    File.Move(renamedEntry, originalPath);
                    logger.LogInformation("Restored original filename: {Path}", originalPath);
                }

                throw new Exception(
                    $"Failed to extract {Path.GetFileName(set.OriginalEntryFile)} (format={format})", ex);
            }
        }

        return extracted;
    }

    /// <summary>
    /// Groups downloaded files into archive sets. Files that form a split archive
    /// are grouped together; standalone archives and non-archives are each their own set.
    /// </summary>
    private List<ArchiveSet> GroupIntoArchiveSets(List<string> files)
    {
        var result = new List<ArchiveSet>();

        // Group by directory + base name (without .partN and extension)
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var key = GetSplitArchiveKey(file);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(file);
        }

        foreach (var (_, groupFiles) in groups)
        {
            // Sort: .exe first (it's the SFX entry), then by name
            var sorted = groupFiles
                .OrderBy(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var entryFile = sorted[0];
            var format = DetectFormat(entryFile);

            // If entry is .exe but we can't detect format from it, try sibling files
            if (format == ArchiveFormat.Unknown && sorted.Count > 1)
            {
                format = InferFormatFromSiblings(sorted);
            }

            // Also try format detection from extension for known archive extensions
            if (format == ArchiveFormat.Unknown)
            {
                format = InferFormatFromExtension(entryFile);
            }

            result.Add(new ArchiveSet
            {
                Files = sorted,
                OriginalEntryFile = sorted[0],
                EntryFile = entryFile,
                Format = format
            });
        }

        return result;
    }

    /// <summary>
    /// Returns a grouping key for split archive detection.
    /// E.g., "VJ006100.part1.exe" and "VJ006100.part2.rar" both return "/full/path/VJ006100".
    /// A standalone "RJ123456.zip" returns "/full/path/RJ123456".
    /// </summary>
    private static string GetSplitArchiveKey(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? "";
        var fileName = Path.GetFileName(filePath);

        // Remove extension
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Remove .partN suffix (case-insensitive)
        var partIdx = name.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (partIdx >= 0)
        {
            var afterPart = name[(partIdx + 5)..];
            if (afterPart.Length > 0 && afterPart.All(char.IsDigit))
            {
                name = name[..partIdx];
            }
        }

        return Path.Combine(dir, name);
    }

    /// <summary>
    /// Detects archive format by reading magic bytes from the file header.
    /// Works for both regular archives and self-extracting .exe files
    /// (SFX .exe files contain the archive header after the PE stub).
    /// </summary>
    private static ArchiveFormat DetectFormat(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            // For SFX executables, the archive signature can be far into the file.
            // We scan up to 1MB which covers all known DLsite SFX stubs.
            var bufferSize = (int)Math.Min(fs.Length, 1024 * 1024);
            var buffer = new byte[bufferSize];
            var bytesRead = fs.Read(buffer, 0, bufferSize);

            // Search for magic bytes in the buffer
            for (var i = 0; i <= bytesRead - 8; i++)
            {
                if (MatchesAt(buffer, i, ZipMagic))
                    return ArchiveFormat.Zip;
                if (MatchesAt(buffer, i, Rar5Magic) || MatchesAt(buffer, i, Rar4Magic))
                    return ArchiveFormat.Rar;
            }
        }
        catch (Exception)
        {
            // File unreadable - fall through to Unknown
        }

        return ArchiveFormat.Unknown;
    }

    private static bool MatchesAt(byte[] buffer, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > buffer.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (buffer[offset + i] != pattern[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// Infers archive format from sibling file extensions (for when the entry file
    /// is an undetectable .exe but other parts have known extensions).
    /// </summary>
    private static ArchiveFormat InferFormatFromSiblings(List<string> files)
    {
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            switch (ext)
            {
                case ".rar":
                    return ArchiveFormat.Rar;
                case ".zip":
                    return ArchiveFormat.Zip;
            }
        }

        return ArchiveFormat.Unknown;
    }

    private static ArchiveFormat InferFormatFromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".zip" => ArchiveFormat.Zip,
            ".rar" => ArchiveFormat.Rar,
            ".7z" or ".lzh" => ArchiveFormat.Rar, // Use 7-Zip path for these
            _ => ArchiveFormat.Unknown
        };
    }

    #region ZIP encoding detection (shared with DLsiteWorkService)

    private void ExtractZipWithEncodingDetection(string zipPath, string outputDir)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = DetectZipEncoding(zipPath);
        logger.LogInformation("Extracting ZIP {Path} with encoding {Encoding}", zipPath, encoding.EncodingName);
        ZipFile.ExtractToDirectory(zipPath, outputDir, encoding, overwriteFiles: true);
    }

    private static readonly Lazy<Encoding[]> EncodingCandidates = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return
        [
            Encoding.UTF8,
            Encoding.GetEncoding(932), // Shift-JIS (Japanese)
            Encoding.GetEncoding(936), // GBK (Simplified Chinese)
            Encoding.GetEncoding(950) // Big5 (Traditional Chinese)
        ];
    });

    // Latin-1 maps each byte 0x00-0xFF to the same Unicode code point, acting as a raw byte passthrough
    private static readonly Lazy<Encoding> Latin1 = new(() => Encoding.GetEncoding(28591));

    private static Encoding DetectZipEncoding(string zipPath)
    {
        if (IsUtf8FlagSet(zipPath))
        {
            return Encoding.UTF8;
        }

        List<byte[]> rawNames;
        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, Latin1.Value);
            rawNames = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => Latin1.Value.GetBytes(e.FullName))
                .ToList();
        }
        catch
        {
            return Encoding.GetEncoding(932);
        }

        if (rawNames.Count == 0)
        {
            return Encoding.GetEncoding(932);
        }

        foreach (var encoding in EncodingCandidates.Value)
        {
            if (IsEncodingValid(rawNames, encoding))
            {
                return encoding;
            }
        }

        return Encoding.GetEncoding(932);
    }

    private static bool IsEncodingValid(List<byte[]> rawNames, Encoding encoding)
    {
        var strictEncoding = (Encoding)encoding.Clone();
        strictEncoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        try
        {
            foreach (var bytes in rawNames)
            {
                strictEncoding.GetString(bytes);
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsUtf8FlagSet(string zipPath)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var reader = new BinaryReader(fs);
            if (fs.Length > 30 && reader.ReadUInt32() == 0x04034b50)
            {
                reader.ReadUInt16(); // version needed to extract
                var flags = reader.ReadUInt16();
                return (flags & (1 << 11)) != 0;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    #endregion

    private class ArchiveSet
    {
        public List<string> Files { get; init; } = [];
        public string OriginalEntryFile { get; init; } = null!;
        public string EntryFile { get; set; } = null!;
        public ArchiveFormat Format { get; init; }
    }
}
