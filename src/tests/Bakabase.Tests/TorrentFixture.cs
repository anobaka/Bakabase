using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using MonoTorrent.BEncoding;

namespace Bakabase.Tests;

/// <summary>
/// Independent BEP 3/47/52 fixtures for the production reader and transfer engine.
/// MonoTorrent's pinned release has a Unix path regression in its unused TorrentCreator API.
/// </summary>
internal static class TorrentFixture
{
    private const int PieceLength = 16 * 1024;

    public static async Task<byte[]> FromPathAsync(string path)
    {
        var files = new Dictionary<string, byte[]>();
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                files[Path.GetRelativePath(path, file)] = await File.ReadAllBytesAsync(file);
        }
        else files[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        return Create(Path.GetFileName(path), files, singleFile: File.Exists(path));
    }

    public static byte[] Create(string name, IReadOnlyDictionary<string, byte[]> files, string? tracker = null,
        bool? hybridFinalPadding = null, bool singleFile = false)
    {
        var ordered = files.Select(file => (Path: file.Key.Replace('\\', '/'), Contents: file.Value))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var entries = new BEncodedList();
        var tree = new BEncodedDictionary();
        var layers = new BEncodedDictionary();
        using var v1Contents = new MemoryStream();
        for (var index = 0; index < ordered.Length; index++)
        {
            var (path, contents) = ordered[index];
            var segments = path.Split('/');
            var encodedPath = new BEncodedList();
            foreach (var segment in segments) encodedPath.Add(new BEncodedString(segment));
            entries.Add(new BEncodedDictionary
            {
                ["length"] = new BEncodedNumber(contents.Length), ["path"] = encodedPath
            });
            v1Contents.Write(contents);
            if (!hybridFinalPadding.HasValue) continue;

            var padding = (PieceLength - contents.Length % PieceLength) % PieceLength;
            if (padding > 0 && (index < ordered.Length - 1 || hybridFinalPadding.Value))
            {
                entries.Add(new BEncodedDictionary
                {
                    ["attr"] = new BEncodedString("p"), ["length"] = new BEncodedNumber(padding),
                    ["path"] = new BEncodedList
                    {
                        new BEncodedString(".pad"),
                        new BEncodedString(padding.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    }
                });
                v1Contents.Write(new byte[padding]);
            }

            var node = tree;
            foreach (var segment in segments)
            {
                if (!node.ContainsKey(segment)) node[segment] = new BEncodedDictionary();
                node = (BEncodedDictionary)node[segment];
            }
            var properties = new BEncodedDictionary { ["length"] = new BEncodedNumber(contents.Length) };
            node[""] = properties;
            if (contents.Length == 0) continue;
            var blockHashes = HashPieces(contents, SHA256.HashData);
            var merkle = blockHashes.Select(hash => hash.ToArray()).ToList();
            var width = 1;
            while (width < merkle.Count) width *= 2;
            while (merkle.Count < width) merkle.Add(new byte[32]);
            while (merkle.Count > 1)
                merkle = Enumerable.Range(0, merkle.Count / 2)
                    .Select(i => SHA256.HashData(merkle[2 * i].Concat(merkle[2 * i + 1]).ToArray())).ToList();
            var root = new BEncodedString(merkle[0]);
            properties["pieces root"] = root;
            if (contents.Length > PieceLength)
                layers[root] = new BEncodedString(blockHashes.SelectMany(hash => hash).ToArray());
        }

        var info = new BEncodedDictionary
        {
            ["name"] = new BEncodedString(name), ["piece length"] = new BEncodedNumber(PieceLength),
            ["pieces"] = new BEncodedString(HashPieces(v1Contents.ToArray(), SHA1.HashData).SelectMany(hash => hash).ToArray())
        };
        if (singleFile) info["length"] = new BEncodedNumber(ordered.Single().Contents.Length);
        else info["files"] = entries;
        var metadata = new BEncodedDictionary { ["info"] = info };
        if (tracker != null) metadata["announce"] = new BEncodedString(tracker);
        if (hybridFinalPadding.HasValue)
        {
            info["meta version"] = new BEncodedNumber(2);
            info["file tree"] = tree;
            metadata["piece layers"] = layers;
        }
        return metadata.Encode();
    }

    private static List<byte[]> HashPieces(byte[] contents, Func<byte[], byte[]> hash)
    {
        var hashes = new List<byte[]>();
        for (var offset = 0; offset < contents.Length; offset += PieceLength)
            hashes.Add(hash(contents.AsSpan(offset, Math.Min(PieceLength, contents.Length - offset)).ToArray()));
        return hashes;
    }
}
