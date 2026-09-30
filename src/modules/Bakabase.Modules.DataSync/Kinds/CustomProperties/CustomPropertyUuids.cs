using System.Security.Cryptography;
using System.Text;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>Option uuids of a custom property's content.</summary>
public static class CustomPropertyUuids
{
    /// <summary>
    /// Every option uuid of <paramref name="content"/> (choices, tags and every node of the tree): the ids the service
    /// already stores, which folding preserves (<see cref="OptionFolding"/>) and a remap must avoid.
    /// </summary>
    public static HashSet<string> OptionUuids(CustomPropertyContentV1 content)
    {
        var uuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uuid in content.Choices.Select(c => c.Uuid).Concat(content.Tags.Select(t => t.Uuid)))
        {
            if (uuid is not null) uuids.Add(uuid);
        }

        var stack = new Stack<CustomPropertyNodeV1>(content.Nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Uuid is not null) uuids.Add(node.Uuid);
            foreach (var child in node.Children) stack.Push(child);
        }

        return uuids;
    }

    /// <summary>
    /// A fresh option uuid for an add whose uuid is already taken in the target property (<c>OptionUuidRemapped</c>,
    /// v3.1 §7.4, §8.5.4 step 2). Derived, not random: planning and applying the same input must agree on the new
    /// uuid, and a plan is a pure function (v3.1 §7.1).
    /// </summary>
    /// <returns>The first uuid derived from <paramref name="uuid"/> that <paramref name="isTaken"/> refuses.</returns>
    public static string Remap(string uuid, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        ArgumentNullException.ThrowIfNull(isTaken);
        for (var attempt = 0; ; attempt++)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"bakabase.dataSync.optionUuid\n{uuid}\n{attempt}"));
            // RFC 4122 layout, version 5 (name-based), variant 1.
            digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
            digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
            var candidate = new Guid(digest.AsSpan(0, 16), bigEndian: true).ToString("D");
            if (!isTaken(candidate)) return candidate;
        }
    }
}
