namespace Bakabase.Modules.DataSync.Identity;

/// <summary>Global identity of one definition: 32 lowercase hex chars (Guid "N"). Never reused, never derived.</summary>
public readonly record struct SyncKey
{
    private const string LinkLevelValue = "00000000000000000000000000000000";

    public string Value { get; }

    public SyncKey(string value)
    {
        if (!IsValid(value)) throw new ArgumentException($"Invalid sync key '{value}'.", nameof(value));
        Value = value;
    }

    /// <summary>
    /// The key of link-level inbox items (<c>LargeChange</c>, whose <c>Kind</c> is <c>""</c>). A sentinel: never
    /// an entity key, and never returned by <see cref="New"/>.
    /// </summary>
    public static SyncKey LinkLevel { get; } = new(LinkLevelValue);

    public static SyncKey New()
    {
        // A version 4 Guid always carries a non-zero version nibble, so this loop never repeats; it states the
        // guarantee instead of relying on it.
        string value;
        do
        {
            value = Guid.NewGuid().ToString("N");
        } while (value == LinkLevelValue);
        return new SyncKey(value);
    }

    public static bool IsValid(string? value) =>
        value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public override string ToString() => Value;
}

/// <summary>
/// Every key an entity is known by: its primary key first, then its alias keys in ordinal order.
/// Entities on the wire always have at least one key. <see cref="None"/> appears only on a
/// CreateEntityOperation that must mint a fresh key (CreateSeparate when an incoming key is bound here).
/// </summary>
public sealed record EntityKeys(IReadOnlyList<SyncKey> All)
{
    public static EntityKeys None { get; } = new(Array.Empty<SyncKey>());
    public SyncKey? Primary => All.Count > 0 ? All[0] : null;
    public bool Contains(SyncKey key) => All.Contains(key);
}
