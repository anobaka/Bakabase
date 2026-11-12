using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A bounded, exact-manifest companion check; never deletes or transforms lyrics/audio.</summary>
internal static class LyricDeliveryProbe
{
    internal const int MaximumBytes = 1024 * 1024;
    private static readonly Regex Timestamp = new(@"^(?:\[[0-9]{1,3}:(?<second>[0-9]{2})(?:[.:][0-9]{1,3})?\])+",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Metadata = new(@"^\[(?:ar|al|ti|au|by|offset|length|re|ve|tool|language):[^\[\]\r\n]*\]$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));

    internal static async Task<bool> IsCompanionAsync(string lyric, IReadOnlyList<string> exactFiles, CancellationToken ct)
    {
        var matching = exactFiles.Where(f => ArtifactManifestFiles.PathComparer.Equals(Path.GetDirectoryName(f), Path.GetDirectoryName(lyric)) &&
            ArtifactManifestFiles.PathComparer.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(lyric)) &&
            Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".flac" or ".wav").ToArray();
        if (matching.Length == 0) return false;
        try
        {
            ArtifactManifestFiles.RejectLinks(lyric);
            var before = new FileInfo(lyric); var length = before.Length; var modified = before.LastWriteTimeUtc;
            if (length is <= 0 or > MaximumBytes) return false;
            await using var stream = new FileStream(lyric, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var bytes = new byte[(int)length]; await stream.ReadExactlyAsync(bytes, ct);
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
            if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return false;
            var lines = text.Split('\n'); if (lines.Length > 4096) return false;
            var timed = 0; var recognized = 0; var nonempty = 0;
            foreach (var raw in lines)
            {
                ct.ThrowIfCancellationRequested();
                var line = raw.Trim(); if (line.Length == 0) continue;
                nonempty++;
                if (line.Length > 16384) return false;
                var time = Timestamp.Match(line);
                if (time.Success && !time.Groups["second"].Captures.Any(c => int.Parse(c.Value, CultureInfo.InvariantCulture) >= 60))
                {
                    timed++; recognized++;
                }
                else if (Metadata.IsMatch(line)) recognized++;
            }
            before.Refresh();
            // Recognition is not lyric editing: retain occasional wrapped text or broken
            // brackets verbatim, but never classify mostly unrelated text as a companion.
            if (timed < 2 || recognized * 10 < nonempty * 9 || before.Length != length || before.LastWriteTimeUtc != modified) return false;
            foreach (var audio in matching)
                if (await IsAudioAsync(audio, ct)) return true;
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or RegexMatchTimeoutException) { return false; }
    }

    private static async Task<bool> IsAudioAsync(string path, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (stream.Length < 16) return false;
        var header = new byte[16]; await stream.ReadExactlyAsync(header, ct);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".flac": return header.AsSpan(0, 4).SequenceEqual("fLaC"u8) && (header[4] & 127) == 0 &&
                                 header[5] == 0 && header[6] == 0 && header[7] == 34 && stream.Length >= 42;
            case ".wav": return header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8) && stream.Length >= 44;
            case ".mp3":
                if (header.AsSpan(0, 3).SequenceEqual("ID3"u8))
                {
                    // https://id3.org/id3v2.3.0 : skip the bounded tag by its synchsafe
                    // size, then require actual MPEG frame bits, not merely an ID3 prefix.
                    if (header[3] is < 2 or > 4 || header[4] == 255 || header.AsSpan(6, 4).ToArray().Any(b => b >= 128)) return false;
                    var size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                    var offset = 10L + size + (header[3] == 4 && (header[5] & 16) != 0 ? 10 : 0);
                    if (offset > stream.Length - 4) return false;
                    stream.Position = offset; await stream.ReadExactlyAsync(header.AsMemory(0, 4), ct);
                }
                return header[0] == 255 && (header[1] & 224) == 224 && ((header[1] >> 3) & 3) != 1 &&
                       ((header[1] >> 1) & 3) == 1 && (header[2] >> 4) is > 0 and < 15 && ((header[2] >> 2) & 3) != 3;
            default: return false;
        }
    }
}
