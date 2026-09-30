using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The shapes of the history columns (§4.1 <c>DataSyncApplyLogs</c>) that the facade reads and every writer of a
/// history entry writes, with <see cref="DataSyncJson.Options"/>:
/// <list type="bullet">
/// <item><c>SummaryJson</c>: a <see cref="DataSyncHistoryCounts"/> object.</item>
/// <item><c>ResultJson</c>: an object whose <c>items</c> member lists <see cref="DataSyncHistoryItem"/>s; the writer
/// may add members of its own (the per-entity change list of §6.5, diagnostics such as <c>transactionMs</c>).</item>
/// <item><c>PreImageJson</c> of an <see cref="DataSyncHistoryKind.EntitySetting"/> entry:
/// <c>{"version":2,"entitySettings":[…]}</c> with one <see cref="DataSyncEntitySettingPreImage"/> per entity.</item>
/// </list>
/// Reads are tolerant: history is shown, never trusted, so a column that does not parse reads as empty.
/// </summary>
public static class DataSyncHistoryJson
{
    public const string ItemsMember = "items";
    public const string EntitySettingsMember = "entitySettings";
    public const int PreImageVersion = 2;

    public static string WriteSummary(DataSyncHistoryCounts counts) =>
        JsonSerializer.Serialize(counts, DataSyncJson.Options);

    public static string WriteResult(IEnumerable<DataSyncHistoryItem> items) =>
        new JsonObject { [ItemsMember] = JsonSerializer.SerializeToNode(items.ToList(), DataSyncJson.Options) }
            .ToJsonString(DataSyncJson.Options);

    public static string WriteEntitySettingPreImage(IEnumerable<DataSyncEntitySettingPreImage> settings) =>
        new JsonObject
        {
            ["version"] = PreImageVersion,
            [EntitySettingsMember] = JsonSerializer.SerializeToNode(settings.ToList(), DataSyncJson.Options),
        }.ToJsonString(DataSyncJson.Options);

    /// <summary>The items of an entry; empty when the column holds none or does not parse.</summary>
    public static IReadOnlyList<DataSyncHistoryItem> ReadItems(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return [];
        try
        {
            var node = JsonNode.Parse(resultJson);
            var items = node switch
            {
                JsonObject o => o[ItemsMember],
                JsonArray a => a,
                _ => null,
            };
            return items?.Deserialize<List<DataSyncHistoryItem>>(DataSyncJson.Options) ?? [];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The entry's counts: its summary, else counted from its items.</summary>
    public static DataSyncHistoryCounts ReadCounts(string? summaryJson, string? resultJson)
    {
        if (!string.IsNullOrWhiteSpace(summaryJson))
        {
            try
            {
                if (JsonNode.Parse(summaryJson) is JsonObject { Count: > 0 } summary &&
                    summary.Deserialize<DataSyncHistoryCounts>(DataSyncJson.Options) is { } counts)
                {
                    return counts;
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
            {
            }
        }

        return CountItems(ReadItems(resultJson));
    }

    public static DataSyncHistoryCounts CountItems(IReadOnlyCollection<DataSyncHistoryItem> items) =>
        new(Created: items.Count(i => i.Action == DataSyncItemAction.Created),
            Updated: items.Count(i => i.Action == DataSyncItemAction.Updated),
            Linked: items.Count(i => i.Action is DataSyncItemAction.Linked or DataSyncItemAction.KeysRecorded),
            Unchanged: items.Count(i => i.Outcome == DataSyncItemOutcome.NoChange),
            Skipped: items.Count(i => i.Outcome == DataSyncItemOutcome.SkippedByUser),
            ChangedSinceReview: items.Count(i => i.Outcome == DataSyncItemOutcome.ChangedSinceReview),
            ChangedDuringApply: items.Count(i => i.Outcome == DataSyncItemOutcome.ChangedDuringApply),
            Held: items.Count(i => i.Outcome == DataSyncItemOutcome.Held),
            Deleted: items.Count(i => i.Action == DataSyncItemAction.Deleted),
            TypeChanged: items.Count(i => i.Action == DataSyncItemAction.TypeChanged),
            Reordered: items.Count(i => i.Action == DataSyncItemAction.Reordered),
            Resolved: 0);
}

/// <summary>
/// What an entity setting changed (§6.6), so undo can put it back (§8.11): the entity's state, its shared
/// <c>childrenLocal</c> field and its overlay before the change.
/// </summary>
public sealed record DataSyncEntitySettingPreImage(string Kind, string LocalKey, DataSyncEntitySyncState State,
    bool ChildrenLocal, DataSyncOverlay Overlay);
