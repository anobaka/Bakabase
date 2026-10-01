using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

/// <summary>Bounds torrent basenames while retaining gallery identity and distinguishing truncated names.</summary>
public static class ExHentaiTorrentFileName
{
    public static string Limit(string fileName, string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        const int componentLimit = 255;
        if (fileName.Length <= componentLimit && Encoding.UTF8.GetByteCount(fileName) <= componentLimit)
            return fileName;

        var galleryId = sourceKey.Split('/', 2)[0];
        if (galleryId.Length is 0 or > 20 || galleryId.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("A gallery ID is required for a shortened torrent filename.", nameof(sourceKey));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey + "\0" + fileName)))
            .ToLowerInvariant()[..16];
        var suffix = $" [g{galleryId}] [{hash}].torrent";
        var byteBudget = componentLimit - Encoding.UTF8.GetByteCount(suffix);
        var characterBudget = componentLimit - suffix.Length;
        var stem = fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".torrent".Length] : fileName;
        var prefix = new StringBuilder();
        foreach (var rune in stem.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > byteBudget || rune.Utf16SequenceLength > characterBudget) break;
            prefix.Append(rune.ToString());
            byteBudget -= rune.Utf8SequenceLength;
            characterBudget -= rune.Utf16SequenceLength;
        }
        return prefix.ToString().TrimEnd(' ', '.') + suffix;
    }
}
