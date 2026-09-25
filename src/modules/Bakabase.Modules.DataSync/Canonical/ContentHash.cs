using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// <c>ContentHash(node) = "sha256:" + lowercase hex(SHA-256(UTF-8 canonical bytes))</c> (v3.1 §3.1).
/// </summary>
public static class ContentHash
{
    public const string Prefix = "sha256:";

    /// <summary>The hash of <paramref name="node"/>'s canonical JSON.</summary>
    public static string Of(JsonNode? node) => OfCanonicalBytes(CanonicalJson.SerializeToUtf8Bytes(node));

    /// <summary>The hash of bytes that already are canonical JSON (a wire page, a stored canonical string).</summary>
    public static string OfCanonicalBytes(ReadOnlySpan<byte> canonicalUtf8) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(canonicalUtf8));

    /// <summary>True for a well-formed hash: the prefix and 64 lowercase hex characters.</summary>
    public static bool IsValid(string? hash) =>
        hash is { Length: 71 } && hash.StartsWith(Prefix, StringComparison.Ordinal) &&
        hash.Skip(Prefix.Length).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
