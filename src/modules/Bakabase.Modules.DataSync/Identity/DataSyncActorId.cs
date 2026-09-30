using System.Security.Cryptography;
using System.Text;

namespace Bakabase.Modules.DataSync.Identity;

/// <summary>
/// One writer identity. 16 lowercase hex = first 8 bytes of
/// SHA-256(UTF-8("bakabase-datasync-actor\n" + nodeId + "\n" + libraryEpoch + "\n" + actorSalt)).
/// actorSalt is 16 random lowercase hex minted at every rotation (§5.6), so an actor id is never derived twice.
/// </summary>
public readonly record struct DataSyncActorId
{
    private const string DerivationPrefix = "bakabase-datasync-actor\n";

    public string Value { get; }

    public DataSyncActorId(string value)
    {
        if (!IsValid(value)) throw new ArgumentException($"Invalid data sync actor id '{value}'.", nameof(value));
        Value = value;
    }

    /// <summary>
    /// Derives the actor of (nodeId, libraryEpoch, actorSalt). The salt must be 16 lowercase hex; nodeId and
    /// libraryEpoch must be non-empty and free of line feeds, so the derivation input stays unambiguous.
    /// </summary>
    public static DataSyncActorId Derive(string nodeId, string libraryEpoch, string actorSalt)
    {
        RequireDerivationPart(nodeId, nameof(nodeId));
        RequireDerivationPart(libraryEpoch, nameof(libraryEpoch));
        if (!IsValid(actorSalt))
            throw new ArgumentException($"An actor salt is 16 lowercase hex characters, got '{actorSalt}'.",
                nameof(actorSalt));

        var input = Encoding.UTF8.GetBytes(DerivationPrefix + nodeId + "\n" + libraryEpoch + "\n" + actorSalt);
        var hash = SHA256.HashData(input);
        return new DataSyncActorId(Convert.ToHexStringLower(hash, 0, 8));
    }

    /// <summary>A fresh 16 lowercase hex salt from the cryptographic random number generator.</summary>
    public static string NewSalt() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public static bool IsValid(string? value) =>
        value is { Length: 16 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public override string ToString() => Value;

    private static void RequireDerivationPart(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
        if (value.Contains('\n'))
            throw new ArgumentException("An actor derivation input must not contain a line feed.", paramName);
    }
}
