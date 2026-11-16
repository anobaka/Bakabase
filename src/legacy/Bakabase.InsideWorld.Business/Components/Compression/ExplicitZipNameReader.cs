using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpZip = SharpCompress.Archives.Zip.ZipArchive;
using DotNetZip = System.IO.Compression.ZipArchive;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>The installed Unix 7-Zip ignores legacy ZIP code pages. Use the existing mature
/// ZIP reader for explicit decoding, with a deliberately bounded supported format.</summary>
internal sealed class ExplicitZipNameReader : IDisposable
{
    private readonly FileStream _input;
    private readonly SharpZip _archive;
    private readonly List<Member> _members = [];
    private sealed record Member(SharpCompress.Archives.Zip.ZipArchiveEntry Entry, string Path,
        long Size, uint Crc, bool Directory);

    internal ExplicitZipNameReader(string path, ArchiveOpenOptions options)
    {
        if (options.TypeArgument != null)
            throw new ArchiveNameEncodingUnsupportedException("Explicit ZIP filename encoding does not support prefixed archives. Use automatic encoding or provide an ordinary ZIP.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding((int)options.ZipFileNameCodePage!.Value,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        _input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> magic = stackalloc byte[4];
            if (_input.Read(magic) != 4 || magic[0] != 'P' || magic[1] != 'K' ||
                !(magic[2] == 3 && magic[3] == 4 || magic[2] == 5 && magic[3] == 6))
                throw new ArchiveNameEncodingUnsupportedException("Explicit ZIP filename encoding requires an ordinary single-volume ZIP without a prefix.");
            _input.Position = 0;
            // .NET exposes central-directory attributes that SharpCompress 0.38 does not.
            using var metadata = new DotNetZip(_input, System.IO.Compression.ZipArchiveMode.Read, true, encoding);
            var headers = metadata.Entries.Select(e => new {e.Length, e.CompressedLength, e.Crc32, e.ExternalAttributes,
                Directory = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')}).ToArray();
            _input.Position = 0;
            _archive = SharpZip.Open(_input, new ReaderOptions {LeaveStreamOpen = true,
                ArchiveEncoding = new ArchiveEncoding {Default = encoding}});
            var entries = _archive.Entries.ToArray();
            if (entries.Length != headers.Length || entries.Length > 100_000)
                throw new InvalidDataException("ZIP entry catalog is inconsistent or exceeds the supported entry count.");
            var paths = new Dictionary<string, (string Spelling, bool Directory, bool Explicit)>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.IsEncrypted)
                    // SharpCompress 0.38 does not authenticate AES trailers. Do not downgrade
                    // encrypted input into an unauthenticated extraction or a password retry.
                    throw new ArchiveNameEncodingUnsupportedException("Explicit ZIP filename encoding currently supports unencrypted ZIP entries only. Automatic encoding retains the normal encrypted-archive support.");
                if (entry.Size < 0 || entry.Size != headers[i].Length || entry.CompressedSize != headers[i].CompressedLength ||
                    entry.IsDirectory != headers[i].Directory || (uint)entry.Crc != headers[i].Crc32)
                    throw new InvalidDataException("ZIP central-directory metadata is inconsistent.");
                if (entry.IsDirectory && (entry.Size != 0 || entry.Crc != 0))
                    throw new InvalidDataException("ZIP directory entries must not contain payload data.");
                var mode = (headers[i].ExternalAttributes >> 16) & 0xf000;
                if (mode != 0 && mode != (entry.IsDirectory ? 0x4000 : 0x8000) ||
                    (headers[i].ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("ZIP links and special filesystem entries are not supported.");
                var name = SafeName(entry.Key, entry.IsDirectory);
                var components = name.Split('/');
                for (var depth = 1; depth <= components.Length; depth++)
                {
                    var prefix = string.Join('/', components.Take(depth));
                    var directory = depth < components.Length || entry.IsDirectory;
                    var explicitEntry = depth == components.Length;
                    var key = prefix.Normalize(NormalizationForm.FormC);
                    if (paths.TryGetValue(key, out var previous))
                    {
                        if (previous.Spelling != prefix || previous.Directory != directory ||
                            explicitEntry && previous.Explicit)
                            throw new InvalidDataException("ZIP contains colliding decoded file or directory names.");
                        if (explicitEntry) paths[key] = (prefix, directory, true);
                    }
                    else paths.Add(key, (prefix, directory, explicitEntry));
                }
                // Opening an entry can replace SharpCompress's central header with a local
                // header. Freeze names, size and CRC before any payload stream is opened.
                _members.Add(new(entry, name, entry.Size, headers[i].Crc32, entry.IsDirectory));
            }

        }
        catch { _archive?.Dispose(); _input.Dispose(); throw; }
    }

    internal List<CompressedFileEntry> Entries() => _members.Where(m => !m.Directory)
        .Select(m => new CompressedFileEntry {Path = m.Path, Size = m.Size}).ToList();

    internal async Task ReadAsync(string? destination, Action<string>? progress, CancellationToken ct)
    {
        if (destination != null)
        {
            var root = new DirectoryInfo(destination);
            if (root.Exists && (root.LinkTarget != null || root.EnumerateFileSystemInfos().Any()))
                throw new InvalidDataException("Explicit ZIP extraction requires an empty private output directory.");
            // Reject linked ancestors as well; every newly created member stays in this tree.
            for (var parent = root; parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("ZIP output directory must not pass through a filesystem link.");
            Directory.CreateDirectory(destination);
        }
        var buffer = new byte[128 * 1024];
        long total = _members.Aggregate(0L, (size, m) => checked(size + m.Size)), completed = 0;
        foreach (var member in _members)
        {
            ct.ThrowIfCancellationRequested();
            var outputPath = destination == null ? null : Path.Combine(destination, member.Path.Replace('/', Path.DirectorySeparatorChar));
            if (member.Directory)
            {
                if (outputPath != null) Directory.CreateDirectory(outputPath);
                continue;
            }
            using var input = OpenMember(member);
            if (member.Entry.IsEncrypted)
                throw new ArchiveNameEncodingUnsupportedException("Encrypted local ZIP entries do not support explicit filename encoding.");
            if (outputPath != null) Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var output = outputPath == null ? Stream.Null :
                new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true);
            var crc = new CRC32();
            long written = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(), ct);
                if (count == 0) break;
                written = checked(written + count);
                if (written > member.Size) throw new InvalidDataException("ZIP entry exceeds its declared uncompressed size.");
                crc.SlurpBlock(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                progress?.Invoke($"{(total == 0 ? 100 : (int)((completed + written) * 100.0 / total))}%");
            }
            if (written != member.Size || (uint)crc.Crc32Result != member.Crc)
                throw new InvalidDataException("ZIP entry failed size or CRC validation.");
            completed += written;
        }
        ct.ThrowIfCancellationRequested();
    }

    private static Stream OpenMember(Member member)
    {
        try { return member.Entry.OpenEntryStream(); }
        catch (CryptographicException)
        {
            throw new ArchiveNameEncodingUnsupportedException("Encrypted local ZIP entries do not support explicit filename encoding.");
        }
        catch (NotSupportedException)
        {
            throw new ArchiveNameEncodingUnsupportedException("This ZIP entry method is not supported with explicit filename encoding. Use automatic encoding or another archive.");
        }
    }

    internal static string SafeName(string? name, bool directory)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\ufffd') || name.Any(char.IsControl))
            throw new InvalidDataException("ZIP contains an invalid decoded entry name.");
        // Decode the whole byte sequence before interpreting separators: a Shift-JIS
        // multibyte character can end in 0x5c without representing a backslash.
        name = name.Replace('\\', '/');
        if (directory && name.EndsWith('/')) name = name[..^1];
        var segments = name.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith(' ') || s.EndsWith('.') ||
                s.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 ||
                IsReserved(s.Split('.')[0])))
            throw new InvalidDataException("ZIP contains an unsafe decoded entry path.");
        return name;
    }

    private static bool IsReserved(string name) => name.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" ||
        name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '0' and <= '9';

    public void Dispose() { _archive.Dispose(); _input.Dispose(); }
}

public sealed class ArchiveNameEncodingUnsupportedException(string message) : InvalidOperationException(message);
