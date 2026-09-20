using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Peers;

namespace Bakabase.Modules.Federation.Security;

/// <summary>A separate wire domain from the legacy full-control device credentials.</summary>
public static class NodeRequestSignature
{
    public const string Scheme = "Bakabase-Node";
    public const int MaxControlBodyBytes = 1024 * 1024;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public sealed record Header(string GrantId, string SubjectNodeId, string AudienceNodeId,
        long Timestamp, string Nonce, string Signature);

    public static bool HasScheme(string? header) =>
        header?.Split(',').Any(value => value.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static Header? Parse(string? value)
    {
        if (value == null || value.Length > 1024 ||
            !value.StartsWith(Scheme + " ", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = value[(Scheme.Length + 1)..].Split(':');
        if (parts.Length != 6 || !IsIdentifier(parts[0]) || !IsIdentifier(parts[1]) ||
            !IsIdentifier(parts[2]) || !IsIdentifier(parts[4]) || !IsIdentifier(parts[5]) ||
            !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)) return null;
        return new Header(parts[0], parts[1], parts[2], timestamp, parts[4], parts[5]);
    }

    public static string Canonical(Header header, string method, string path, string rawQuery, string bodyDigest) =>
        string.Join('\n', Scheme, "1", header.GrantId, header.SubjectNodeId, header.AudienceNodeId,
            method.ToUpperInvariant(), path, rawQuery,
            header.Timestamp.ToString(CultureInfo.InvariantCulture), header.Nonce, bodyDigest);

    public static string Create(NodeCredentials credentials, string method, string path, string rawQuery,
        string bodyDigest, DateTimeOffset now, string? nonce = null)
    {
        var header = new Header(credentials.GrantId, credentials.SubjectNodeId, credentials.AudienceNodeId,
            now.ToUnixTimeSeconds(), nonce ?? RandomToken(18), "");
        var signature = Mac(credentials.Key, Canonical(header, method, path, rawQuery, bodyDigest));
        return $"{Scheme} {header.GrantId}:{header.SubjectNodeId}:{header.AudienceNodeId}:" +
               $"{header.Timestamp.ToString(CultureInfo.InvariantCulture)}:{header.Nonce}:{signature}";
    }

    public static string Hash(ReadOnlySpan<byte> body) => Base64Url(SHA256.HashData(body));
    public static string HashSecret(string? secret) => Hash(Encoding.UTF8.GetBytes(secret ?? ""));
    public static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    public static string Mac(string key, string message) =>
        Base64Url(HMACSHA256.HashData(Decode(key), Encoding.UTF8.GetBytes(message)));

    public static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    public static string HandshakeProof(string key, NodeInfo info, string challenge) =>
        Mac(key, "Bakabase-Node-handshake-v1\n" + challenge + "\n" +
                 JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
