using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Recognize paired recording and timed-comment files in the exact delivery manifest.</summary>
internal static class LiveRecordingDeliveryProbe
{
    internal const int MaximumXmlBytes = 2 * 1024 * 1024;

    internal static async Task<bool> ArePairsAsync(IEnumerable<string> files, CancellationToken ct)
    {
        var recordings = files.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".flv" or ".xml");
        foreach (var pair in recordings.GroupBy(f => Path.ChangeExtension(f, null), ArtifactManifestFiles.PathComparer))
        {
            var members = pair.ToArray();
            if (members.Length != 2) return false;
            var video = members.FirstOrDefault(f => Path.GetExtension(f).Equals(".flv", StringComparison.OrdinalIgnoreCase));
            var comments = members.FirstOrDefault(f => Path.GetExtension(f).Equals(".xml", StringComparison.OrdinalIgnoreCase));
            if (video == null || comments == null) return false;
            try
            {
                ArtifactManifestFiles.RejectLinks(video);
                ArtifactManifestFiles.RejectLinks(comments);
                if (!await IsVideoAsync(video, ct) || !await IsTimedChatAsync(comments, ct)) return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException)
            {
                return false;
            }
        }
        return true;
    }

    // Adobe FLV, Annex E: the previous-tag size points back to its 11-byte header.
    // https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/flvdec.c
    private static async Task<bool> IsVideoAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        var header = new byte[13];
        if (file.Length < header.Length) return false;
        await file.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual("FLV\x01"u8) || header[4] is not (1 or 5) ||
            BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5)) != 9 ||
            BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(9)) != 0) return false;
        // A partial transfer can retain an intact first frame. The last tag must also end
        // at the advertised boundary; this checks its framing without reading video payloads.
        file.Position = file.Length - 4;
        await file.ReadExactlyAsync(header.AsMemory(0, 4), ct);
        var lastTagSize = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (lastTagSize < 12 || lastTagSize > file.Length - 17) return false;
        file.Position = file.Length - 4 - lastTagSize;
        await file.ReadExactlyAsync(header.AsMemory(0, 11), ct);
        if (header[0] is not (8 or 9 or 18) ||
            ((header[1] << 16) | (header[2] << 8) | header[3]) + 11 != lastTagSize ||
            header[8] != 0 || header[9] != 0 || header[10] != 0) return false;
        file.Position = 13;
        for (var tag = 0; tag < 32 && file.Length - file.Position >= 16; tag++)
        {
            await file.ReadExactlyAsync(header.AsMemory(0, 12), ct);
            var kind = header[0];
            var size = (header[1] << 16) | (header[2] << 8) | header[3];
            if (kind is not (8 or 9 or 18) || size == 0 || size + 3L > file.Length - file.Position ||
                header[8] != 0 || header[9] != 0 || header[10] != 0) return false;
            var codec = header[11] & 15;
            var isVideo = kind == 9 && size > 5 && (header[11] >> 4) is >= 1 and <= 4 && codec is >= 1 and <= 7;
            var payloadEnd = file.Position + size - 1;
            if (isVideo && codec == 7)
            {
                await file.ReadExactlyAsync(header.AsMemory(0, 1), ct);
                isVideo = header[0] == 1; // AVC media packet, rather than codec setup or an end marker.
            }
            file.Position = payloadEnd;
            await file.ReadExactlyAsync(header.AsMemory(0, 4), ct);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != size + 11) return false;
            if (isVideo) return true;
        }
        return false;
    }

    // Bilibili-compatible XML recordings store timed comments as direct children i/d.
    // https://github.com/BililiveRecorder/BililiveRecorder
    private static async Task<bool> IsTimedChatAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > MaximumXmlBytes) return false;
        using var reader = XmlReader.Create(file, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes, IgnoreComments = true, IgnoreWhitespace = true
        });
        if (await reader.MoveToContentAsync() != XmlNodeType.Element || reader.Name != "i" || reader.NamespaceURI.Length != 0) return false;
        var count = 0;
        var timedComment = false;
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.Depth <= 2)
            {
                timedComment = false;
                if (reader.Depth != 1 || reader.Name != "d" || reader.NamespaceURI.Length != 0) continue;
                var values = reader.GetAttribute("p")?.Split(',');
                if (values is not { Length: >= 8 } ||
                    !double.TryParse(values[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
                    !double.IsFinite(seconds) || seconds < 0 ||
                    !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mode) || mode is < 1 or > 9)
                    return false;
                timedComment = true;
            }
            else if (timedComment && reader.Depth == 2 && reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA &&
                     !string.IsNullOrWhiteSpace(reader.Value))
            {
                count++;
                timedComment = false;
            }
            else if (reader.Depth <= 1) timedComment = false;
        }
        return count >= 2;
    }
}
