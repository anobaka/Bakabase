using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// Unknown-field preservation (§8.9): the top-level content members a codec does not know are kept verbatim
/// (<c>UnknownJson</c>), merged member by member against the base's with the scalar table of §8.5.2, and merged
/// back when publishing (§3.5 step 5). So an older build in a two-way link never regresses or erases a member a
/// newer build added at the top level of the content.
/// </summary>
public static class DataSyncUnknownMembers
{
    /// <summary>The merge path of one unknown member (§8.5.1): <c>x:{member}</c>. Never an inbox item.</summary>
    public const string PathPrefix = "x:";

    /// <summary>
    /// Merges one entity's unknown members:
    /// <list type="bullet">
    /// <item><c>ThreeWay</c>: per member, with an absent member as a value: unchanged on one side → the other
    /// side's; changed on both to different values → the local value is kept and the member is reported in
    /// <see cref="DataSyncUnknownMergeResult.ConflictsKeptLocal"/> for a history note;</item>
    /// <item><c>FastForward</c> and <c>Convert</c>: the peer's members (a member the peer no longer has is gone);</item>
    /// <item><c>NoBase</c>: the union, the peer's value where both have one.</item>
    /// </list>
    /// </summary>
    public static DataSyncUnknownMergeResult Merge(JsonObject? baseUnknown, JsonObject? local, JsonObject? remote,
        DataSyncMerge3Mode mode3)
    {
        var fields = new List<DataSyncFieldOutcome>();
        var conflicts = new List<string>();
        var merged = new JsonObject();
        var names = Names(local).Union(Names(remote)).Union(mode3 == DataSyncMerge3Mode.ThreeWay ? Names(baseUnknown) : [])
            .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
        foreach (var name in names)
        {
            var l = Value(local, name);
            var r = Value(remote, name);
            var b = Value(baseUnknown, name);
            JsonNode? result;
            DataSyncFieldResolution resolution;
            switch (mode3)
            {
                case DataSyncMerge3Mode.FastForward:
                case DataSyncMerge3Mode.Convert:
                    result = r;
                    resolution = DataSyncFieldResolution.TookRemote;
                    break;
                case DataSyncMerge3Mode.NoBase:
                    result = r ?? l;
                    resolution = r is not null ? DataSyncFieldResolution.TookRemote : DataSyncFieldResolution.KeptLocal;
                    break;
                default:
                    if (Same(l, r))
                    {
                        result = l;
                        resolution = DataSyncFieldResolution.Unchanged;
                    }
                    else if (Same(b, l))
                    {
                        result = r;
                        resolution = DataSyncFieldResolution.TookRemote;
                    }
                    else if (Same(b, r))
                    {
                        result = l;
                        resolution = DataSyncFieldResolution.KeptLocal;
                    }
                    else
                    {
                        result = l;
                        resolution = DataSyncFieldResolution.KeptLocal;
                        conflicts.Add(name);
                    }

                    break;
            }

            if (result is not null) merged[name] = result.DeepClone();
            if (!Same(l, r))
            {
                fields.Add(new DataSyncFieldOutcome(PathPrefix + name, resolution, Display(b), Display(l), Display(r),
                    Display(result)));
            }
        }

        return new DataSyncUnknownMergeResult(merged.Count == 0 ? null : merged, fields, conflicts);
    }

    /// <summary>Whether two sets of unknown members are equal (null and empty alike).</summary>
    public static bool AreEqual(JsonObject? a, JsonObject? b) =>
        (a is null || a.Count == 0) && (b is null || b.Count == 0) || JsonNode.DeepEquals(a, b);

    private static IEnumerable<string> Names(JsonObject? members) => members?.Select(m => m.Key) ?? [];

    /// <summary>The member's value; a JSON null reads as absent (canonical content has no nulls).</summary>
    private static JsonNode? Value(JsonObject? members, string name) => members?[name];

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    private static DataSyncDisplayValue? Display(JsonNode? value) =>
        value is null ? null : new DataSyncDisplayValue(CanonicalJson.Serialize(value));
}

/// <param name="Merged">The merged members; null when none are left.</param>
/// <param name="Fields">One <c>x:{member}</c> outcome per member the two sides differ on.</param>
/// <param name="ConflictsKeptLocal">Members both sides changed differently: the local value was kept (a history note).</param>
public sealed record DataSyncUnknownMergeResult(JsonObject? Merged, IReadOnlyList<DataSyncFieldOutcome> Fields,
    IReadOnlyList<string> ConflictsKeptLocal);
