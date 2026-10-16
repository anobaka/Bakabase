using System;
using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Process-local pagination tokens bound to the owning device, library incarnation and parent.
/// A restart or library reset invalidates the tree cursor; saved prefix mappings remain intact.
/// </summary>
public sealed class ManagedLibraryMappingTreeTokens : IDisposable
{
    public const int MaximumTokenChars = 12288;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _time;
    public ManagedLibraryMappingTreeTokens(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public string Issue(NodeIdentity identity, string context, string value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Payload(value, _time.GetUtcNow().AddMinutes(20).ToUnixTimeSeconds()));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Associated(identity, context));
        var bytes = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(bytes, 0);
        tag.CopyTo(bytes, nonce.Length);
        ciphertext.CopyTo(bytes, nonce.Length + tag.Length);
        var result = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (result.Length > MaximumTokenChars) throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
        return result;
    }

    public string Read(NodeIdentity identity, string context, string token)
    {
        ValidateInput(token);
        try
        {
            var bytes = NodeRequestSignature.Decode(token);
            if (bytes.Length < 29) throw new CryptographicException();
            var plaintext = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext,
                Associated(identity, context));
            var payload = JsonSerializer.Deserialize<Payload>(plaintext);
            if (payload == null || payload.ExpiresAt <= _time.GetUtcNow().ToUnixTimeSeconds())
                throw new CryptographicException();
            return payload.Value;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            throw new FederationQueryException("MappingTreeExpired", 409,
                "Reopen the directory tree to refresh its browsing session.", innerException: e);
        }
    }

    public static void ValidateInput(string? value)
    {
        if (value != null && (value.Length is 0 or > MaximumTokenChars ||
                             !System.Linq.Enumerable.All(value, c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new FederationQueryException("InvalidMappingTreeRequest", 422);
    }

    private static byte[] Associated(NodeIdentity identity, string context) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Domain = "Bakabase-managed-mapping-tree-v1", identity.NodeId,
            identity.LibraryEpoch, Context = context });
    private sealed record Payload(string Value, long ExpiresAt);
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
