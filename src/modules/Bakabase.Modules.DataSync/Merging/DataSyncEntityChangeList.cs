using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// What an apply changed on one entity, by path and by child id (§6.5, §8.11 pre-images version 2): scalar paths
/// with their before and after values, and children added, removed, renamed, recoloured and moved. The apply
/// runner writes it to the history's <c>ResultJson</c>; the lost-update guard and undo read it back.
/// </summary>
/// <remarks>
/// Scalars are read from the codec's canonical content: every top-level member that is not an array, every member
/// of a nested object as <c>parent.member</c> (<c>settings.precision</c>), and <c>defaultValue</c> as a whole.
/// Other arrays are child lists and are compared through <see cref="IDataSyncKindCodec.ChildrenOf"/>, by id.
/// </remarks>
public sealed record DataSyncEntityChangeList(
    IReadOnlyList<DataSyncScalarChange> Scalars,
    IReadOnlyList<DataSyncChildSnapshot> Added,
    IReadOnlyList<DataSyncChildSnapshot> Removed,
    IReadOnlyList<DataSyncChildChange> Renamed,
    IReadOnlyList<DataSyncChildChange> Recolored,
    IReadOnlyList<DataSyncChildMove> Moved)
{
    public static DataSyncEntityChangeList Empty { get; } = new([], [], [], [], [], []);

    public bool IsEmpty => Scalars.Count == 0 && Added.Count == 0 && Removed.Count == 0 && Renamed.Count == 0 &&
                           Recolored.Count == 0 && Moved.Count == 0;

    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/> (both <c>ReadLocal</c> content).</summary>
    public static DataSyncEntityChangeList Between(IDataSyncKindCodec codec, object before, object after)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var beforeScalars = ScalarsOf(codec.Write(before));
        var afterScalars = ScalarsOf(codec.Write(after));
        var scalars = beforeScalars.Keys.Union(afterScalars.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new DataSyncScalarChange(p, beforeScalars.GetValueOrDefault(p)?.DeepClone(),
                afterScalars.GetValueOrDefault(p)?.DeepClone()))
            .Where(c => !JsonNode.DeepEquals(c.Before, c.After))
            .ToList();

        var beforeChildren = ChildrenById(codec, before);
        var afterChildren = ChildrenById(codec, after);
        var added = afterChildren.Values.Where(c => !beforeChildren.ContainsKey(c.Id)).Select(Snapshot).ToList();
        var removed = beforeChildren.Values.Where(c => !afterChildren.ContainsKey(c.Id)).Select(Snapshot).ToList();
        var renamed = new List<DataSyncChildChange>();
        var recolored = new List<DataSyncChildChange>();
        var moved = new List<DataSyncChildMove>();
        foreach (var (id, was) in beforeChildren)
        {
            if (!afterChildren.TryGetValue(id, out var now)) continue;
            if (was.Display.Text != now.Display.Text || was.Display.Group != now.Display.Group)
                renamed.Add(new DataSyncChildChange(id, was.Display, now.Display));
            if (was.Display.Color != now.Display.Color) recolored.Add(new DataSyncChildChange(id, was.Display, now.Display));
            if (was.ParentId != now.ParentId) moved.Add(new DataSyncChildMove(id, was.ParentId, now.ParentId));
        }

        return new DataSyncEntityChangeList(scalars, added, removed, renamed, recolored, moved);
    }

    /// <summary>The scalar paths of canonical content (see the remarks).</summary>
    public static IReadOnlyDictionary<string, JsonNode?> ScalarsOf(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var scalars = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (name, value) in content)
        {
            switch (value)
            {
                case JsonArray when name == "defaultValue":
                    scalars[name] = value;
                    break;
                case JsonArray:
                    break;
                case JsonObject nested:
                    foreach (var (member, inner) in nested) scalars[name + "." + member] = inner;
                    break;
                default:
                    scalars[name] = value;
                    break;
            }
        }

        return scalars;
    }

    private static Dictionary<string, DataSyncChildInfo> ChildrenById(IDataSyncKindCodec codec, object content)
    {
        var result = new Dictionary<string, DataSyncChildInfo>(StringComparer.Ordinal);
        foreach (var child in codec.ChildrenOf(content))
        {
            // A child without an id (a null uuid) cannot be followed by id.
            if (!string.IsNullOrEmpty(child.Id)) result.TryAdd(child.Id, child);
        }

        return result;
    }

    private static DataSyncChildSnapshot Snapshot(DataSyncChildInfo child) => new(child.Id, child.ParentId, child.Display);
}

/// <param name="Before">The canonical value before; null when absent.</param>
/// <param name="After">The canonical value after; null when absent.</param>
public sealed record DataSyncScalarChange(string Path, JsonNode? Before, JsonNode? After);

public sealed record DataSyncChildSnapshot(string ChildId, string? ParentId, DataSyncDisplayValue Display);

public sealed record DataSyncChildChange(string ChildId, DataSyncDisplayValue Before, DataSyncDisplayValue After);

public sealed record DataSyncChildMove(string ChildId, string? ParentBefore, string? ParentAfter);

/// <summary>
/// The lost-update guard (§6.5). A whole-row writer that read an entity before a sync apply committed and wrote it
/// back afterwards silently overwrites what sync applied; a plain Refresh would publish that stale overwrite as a
/// newer local revision and revert the change on every device. The guard compares the current content with the
/// most recent apply's change list and flags every applied change the content <b>undoes</b>.
/// </summary>
public static class DataSyncLostUpdateGuard
{
    /// <summary>How long after an apply a revert is suspected (§6.5).</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The history kinds whose changes the guard protects: <c>AutoSync</c>, <c>Resolution</c> and <c>FirstLink</c>.
    /// Data sync's own undo is exempt (its logs are <c>Undo</c>).
    /// </summary>
    public static bool Covers(DataSyncHistoryKind kind) =>
        kind is DataSyncHistoryKind.AutoSync or DataSyncHistoryKind.Resolution or DataSyncHistoryKind.FirstLink;

    /// <summary>Whether an apply at <paramref name="appliedAtUtc"/> is still inside the window at <paramref name="nowUtc"/>.</summary>
    public static bool InWindow(DateTime appliedAtUtc, DateTime nowUtc) =>
        nowUtc >= appliedAtUtc && nowUtc - appliedAtUtc <= Window;

    /// <summary>
    /// The applied changes <paramref name="current"/> undoes (empty: not suspect): a scalar back at its before value
    /// (before ≠ after); an added child's id gone; a renamed child's id carrying its old label; a moved node back
    /// under its old parent; a removed child's id back.
    /// </summary>
    public static DataSyncEntityChangeList UndoneChanges(IDataSyncKindCodec codec, object current,
        DataSyncEntityChangeList applied)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(applied);
        var scalars = DataSyncEntityChangeList.ScalarsOf(codec.Write(current));
        var children = codec.ChildrenOf(current).Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var undoneScalars = applied.Scalars
            .Where(s => !JsonNode.DeepEquals(s.Before, s.After) &&
                        JsonNode.DeepEquals(scalars.GetValueOrDefault(s.Path), s.Before))
            .ToList();
        var undoneAdds = applied.Added.Where(a => !children.ContainsKey(a.ChildId)).ToList();
        var undoneRemovals = applied.Removed.Where(r => children.ContainsKey(r.ChildId)).ToList();
        var undoneRenames = applied.Renamed
            .Where(r => children.TryGetValue(r.ChildId, out var c) && c.Display.Text == r.Before.Text &&
                        c.Display.Group == r.Before.Group &&
                        (r.Before.Text != r.After.Text || r.Before.Group != r.After.Group))
            .ToList();
        var undoneMoves = applied.Moved
            .Where(m => m.ParentBefore != m.ParentAfter && children.TryGetValue(m.ChildId, out var c) &&
                        c.ParentId == m.ParentBefore)
            .ToList();
        return new DataSyncEntityChangeList(undoneScalars, undoneAdds, undoneRemovals, undoneRenames, [], undoneMoves);
    }

    /// <summary>
    /// The state-derived <c>SuspectedLostUpdate</c> item (§9.1 J): it belongs to no link, its subject is the whole
    /// entity, and its fields list the undone changes — a scalar by its path (before, current, synced value), a child
    /// as <c>child:{id}</c> (a move as <c>child:{id}:parent</c>) — so the card can say what the write reverted.
    /// </summary>
    public static DataSyncInboxDraft Draft(string kind, Identity.EntityKeys keys, string localKey, string entityName,
        string? subtype, DataSyncEntityChangeList undone, Identity.DataSyncVersionVector localVv)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(undone);
        var fields = new List<DataSyncFieldOutcome>();
        foreach (var s in undone.Scalars)
        {
            fields.Add(new DataSyncFieldOutcome(s.Path, DataSyncFieldResolution.TookRemote, Show(s.Before), Show(s.Before),
                Show(s.After), Show(s.After)));
        }

        foreach (var a in undone.Added)
            fields.Add(new DataSyncFieldOutcome("child:" + a.ChildId, DataSyncFieldResolution.TookRemote, null, null, a.Display, a.Display));
        foreach (var r in undone.Removed)
            fields.Add(new DataSyncFieldOutcome("child:" + r.ChildId, DataSyncFieldResolution.TookRemote, r.Display, r.Display, null, null));
        foreach (var r in undone.Renamed)
            fields.Add(new DataSyncFieldOutcome("child:" + r.ChildId, DataSyncFieldResolution.TookRemote, r.Before, r.Before, r.After, r.After));
        foreach (var m in undone.Moved)
        {
            fields.Add(new DataSyncFieldOutcome("child:" + m.ChildId + ":parent", DataSyncFieldResolution.TookRemote,
                Parent(m.ParentBefore), Parent(m.ParentBefore), Parent(m.ParentAfter), Parent(m.ParentAfter)));
        }

        var payload = new DataSyncInboxPayload(entityName, subtype, null, null, null, fields, null, null, null, 0, null,
            null, null, null, null);
        return DataSyncInboxDrafts.Create(kind, keys.Primary ?? throw new ArgumentException("No key.", nameof(keys)),
            localKey, DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxDrafts.EntitySubject, payload, null, null,
            localVv, DataSyncMergeFlags.None);
    }

    private static DataSyncDisplayValue? Show(JsonNode? value) =>
        value is null ? null : new DataSyncDisplayValue(value is JsonValue v && v.TryGetValue(out string? s) ? s : CanonicalJson.Serialize(value));

    private static DataSyncDisplayValue Parent(string? parentId) => new(parentId ?? "");
}
