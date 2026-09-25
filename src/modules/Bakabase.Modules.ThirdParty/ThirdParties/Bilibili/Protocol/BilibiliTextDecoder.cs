using System.IO.Compression;
using System.Text;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>
/// Decodes bodies fetched without automatic decompression: <c>comment.bilibili.com</c> sends danmaku XML as RAW
/// deflate (a zlib reader fails with "incorrect header check") and subtitle JSON is gzip-compressed.
/// </summary>
public static class BilibiliTextDecoder
{
    /// <summary>Decoded output is capped; a larger body is refused (<see cref="InvalidDataException"/>).</summary>
    public const int MaxDecodedBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Undoes <paramref name="contentEncodings"/> (the <c>Content-Encoding</c> values, in header order; they are
    /// applied in reverse): identity, gzip, deflate (zlib or raw), br. Unknown encodings throw
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static byte[] Decode(byte[] body, IEnumerable<string> contentEncodings)
    {
        var encodings = SplitEncodings(contentEncodings);
        var current = body;
        for (var i = encodings.Count - 1; i >= 0; i--)
        {
            current = encodings[i] switch
            {
                "identity" => current,
                "gzip" or "x-gzip" => Inflate(current, s => new GZipStream(s, CompressionMode.Decompress)),
                "deflate" => IsZlib(current)
                    ? Inflate(current, s => new ZLibStream(s, CompressionMode.Decompress))
                    : Inflate(current, s => new DeflateStream(s, CompressionMode.Decompress)),
                "br" => Inflate(current, s => new BrotliStream(s, CompressionMode.Decompress)),
                var unknown => throw new InvalidDataException($"Unsupported content encoding '{Truncate(unknown)}'."),
            };
        }

        if (current.Length > MaxDecodedBytes)
        {
            throw new InvalidDataException($"Body larger than {MaxDecodedBytes} bytes.");
        }

        return current;
    }

    /// <summary>
    /// Danmaku XML as text: decoded, BOM stripped; when no encoding was declared but the body is not XML, raw
    /// inflate is tried once. Throws <see cref="InvalidDataException"/> unless the result looks like danmaku
    /// (<c>&lt;i</c>).
    /// </summary>
    public static string DecodeDanmakuXml(byte[] body, IEnumerable<string> contentEncodings)
    {
        var encodings = SplitEncodings(contentEncodings);
        var text = ToText(Decode(body, encodings));
        if (!text.TrimStart().StartsWith('<') && encodings.All(e => e == "identity"))
        {
            try
            {
                text = ToText(Inflate(body, s => new DeflateStream(s, CompressionMode.Decompress)));
            }
            catch (InvalidDataException)
            {
                // Not deflate either: reported below.
            }
        }

        if (!text.Contains("<i", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The danmaku body is not XML.");
        }

        return text;
    }

    private static List<string> SplitEncodings(IEnumerable<string> contentEncodings) =>
        contentEncodings
            .SelectMany(e => e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(e => e.ToLowerInvariant())
            .ToList();

    private static string ToText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>RFC 1950 header: CM = 8 and the 16-bit header is a multiple of 31.</summary>
    private static bool IsZlib(byte[] body) =>
        body.Length >= 2 && (body[0] & 0x0F) == 8 && ((body[0] << 8) | body[1]) % 31 == 0;

    private static byte[] Inflate(byte[] input, Func<Stream, Stream> decompressor)
    {
        using var source = new MemoryStream(input, false);
        using var stream = decompressor(source);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxDecodedBytes)
            {
                throw new InvalidDataException($"Decoded body larger than {MaxDecodedBytes} bytes.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string Truncate(string value) => value.Length <= 32 ? value : value[..32];
}
