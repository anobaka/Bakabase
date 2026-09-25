using System.Globalization;

namespace Bakabase.Modules.DataSync.Kinds.ExtensionGroups;

/// <summary>
/// Kind <c>extensionGroup</c>, schemaVersion 1 (v3.1 §3.4): <c>{"extensions":[".avi",".mkv",".mp4"],"name":"Video"}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Extensions"/> are canonical: each trimmed, given exactly one leading dot and lowercased (invariant),
/// deduplicated and sorted ordinally; an empty list when there are none. Content has no ids: extensions are
/// values, so comparison is on canonical content and a stored <c>.MKV</c> equals an incoming <c>.mkv</c>.
/// </para>
/// <para>
/// The constructor keeps the values it is given (sorted and deduplicated), so local content read back with
/// <c>ReadLocal</c> is never altered; <see cref="FromLocal"/> builds content from stored raw extensions.
/// </para>
/// </remarks>
public sealed class ExtensionGroupContentV1 : IEquatable<ExtensionGroupContentV1>
{
    public ExtensionGroupContentV1(string name, IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extensions);
        Name = name;
        var sorted = extensions.Select(e => e ?? throw new ArgumentException("An extension is never null.",
            nameof(extensions))).Distinct(StringComparer.Ordinal).ToList();
        sorted.Sort(StringComparer.Ordinal);
        Extensions = sorted;
    }

    public string Name { get; }

    /// <summary>Canonical extensions, distinct and in ordinal order.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Content for a stored group (the adapter's read): every raw extension canonicalized; blank ones are left
    /// out, as the extension group service itself does.
    /// </summary>
    public static ExtensionGroupContentV1 FromLocal(string name, IEnumerable<string?>? rawExtensions) =>
        new(name, (rawExtensions ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => Canonicalize(e!)));

    /// <summary>One extension's canonical form: trimmed, exactly one leading dot, lowercased (invariant).</summary>
    public static string Canonicalize(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return "." + extension.Trim().TrimStart('.').ToLower(CultureInfo.InvariantCulture);
    }

    public bool Equals(ExtensionGroupContentV1? other) =>
        other is not null && (ReferenceEquals(this, other) ||
                              (Name == other.Name && Extensions.SequenceEqual(other.Extensions, StringComparer.Ordinal)));

    public override bool Equals(object? obj) => obj is ExtensionGroupContentV1 other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var extension in Extensions) hash.Add(extension, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => $"{Name} [{string.Join(", ", Extensions)}]";
}
