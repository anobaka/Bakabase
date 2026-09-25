using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// The plan's fixed forms (v3.1 §7.5): change and warning order, counts, review tokens and the plan id. Everything
/// here is a function of its arguments, so planning the same input twice gives the same bytes.
/// </summary>
internal static class DataSyncPlanFormat
{
    /// <summary>Group prefixes a decision may exclude even when the item has no change of that group (v3.1 §7.4).</summary>
    public static readonly IReadOnlySet<string> WellKnownGroupPrefixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "choice:add", "choice:rename", "choice:recolor", "tag:add", "tag:rename", "tag:recolor", "node:add",
        "node:rename", "node:recolor", "ext:add",
    };

    /// <summary>An option or extension change; everything else (name, flags, settings, defaultValue) is a scalar.</summary>
    public static bool IsChild(DataSyncFieldChange change) => change.Kind is DataSyncFieldChangeKind.AddChild
        or DataSyncFieldChangeKind.RenameChild or DataSyncFieldChangeKind.RecolorChild
        or DataSyncFieldChangeKind.AddMember or DataSyncFieldChangeKind.RemoveChild or DataSyncFieldChangeKind.MoveChild;

    /// <summary>
    /// v3.1 §7.5: by path group — <c>name, ignoreCase, childrenLocal, settings.*, defaultValue</c>, other scalars, then
    /// <c>choices|tags|nodes</c>, other children, <c>extensions</c> — keeping the codec's order (incoming child
    /// position) within a group. Every scalar therefore comes first.
    /// </summary>
    public static IReadOnlyList<DataSyncFieldChange> SortChanges(IEnumerable<DataSyncFieldChange> changes) =>
        changes.OrderBy(GroupRank).ToList();

    /// <summary>v3.1 §7.5: by code, then change id (item-level first), keeping the codec's order otherwise.</summary>
    public static IReadOnlyList<DataSyncPlanWarning> SortWarnings(IEnumerable<DataSyncPlanWarning> warnings) =>
        warnings.OrderBy(w => w.Code).ThenBy(w => w.ChangeId, StringComparer.Ordinal).ToList();

    private static int GroupRank(DataSyncFieldChange change)
    {
        if (IsChild(change))
        {
            return change.Path switch
            {
                "choices" or "tags" or "nodes" => 10,
                "extensions" => 12,
                _ => 11,
            };
        }

        return change.Path switch
        {
            "name" => 0,
            "ignoreCase" => 1,
            "childrenLocal" => 2,
            "defaultValue" => 4,
            _ when change.Path.StartsWith("settings.", StringComparison.Ordinal) => 3,
            _ => 5,
        };
    }

    public static DataSyncChangeCounts CountChanges(IReadOnlyCollection<DataSyncFieldChange> changes) => new(
        changes.Count,
        changes.Count(c => c.Kind is DataSyncFieldChangeKind.Set or DataSyncFieldChangeKind.SetType),
        changes.Count(c => c.Kind is DataSyncFieldChangeKind.AddChild or DataSyncFieldChangeKind.AddMember),
        changes.Count(c => c.Kind == DataSyncFieldChangeKind.RenameChild),
        changes.Count(c => c.Kind == DataSyncFieldChangeKind.RecolorChild));

    public static IReadOnlyList<DataSyncWarningCount> CountWarnings(IEnumerable<DataSyncPlanWarning> warnings) =>
        warnings.GroupBy(w => w.Code).OrderBy(g => g.Key).Select(g => new DataSyncWarningCount(g.Key, g.Count()))
            .ToList();

    // ---- tokens and ids -------------------------------------------------------------------------

    /// <summary>
    /// v3.1 §7.5: the first 32 hex characters of SHA-256 over the canonical JSON of
    /// <c>{type, reason, localKey, incomingHash, changes:[{changeId, from, to}]}</c> with the <b>full</b> change list.
    /// Neither the local content hash nor any count is included, so a local change the review does not touch leaves
    /// the token as it was (M2).
    /// </summary>
    public static string ReviewToken(DataSyncPlanItemType type, DataSyncPlanItemReason? reason, string? localKey,
        string? incomingHash, IEnumerable<DataSyncFieldChange> changes)
    {
        var json = new JsonObject { ["type"] = type.ToString() };
        if (reason is { } r) json["reason"] = r.ToString();
        if (localKey is not null) json["localKey"] = localKey;
        if (incomingHash is not null) json["incomingHash"] = incomingHash;
        json["changes"] = new JsonArray(changes.Select(c =>
        {
            var change = new JsonObject { ["changeId"] = c.ChangeId };
            if (c.From is { } from) change["from"] = Display(from);
            if (c.To is { } to) change["to"] = Display(to);
            return (JsonNode?)change;
        }).ToArray());
        return Sha256Hex(CanonicalJson.SerializeToUtf8Bytes(json))[..32];
    }

    /// <summary>v3.1 §7.5: 16 hex characters over the full plan with an empty PlanId and every InUseCount null.</summary>
    public static string PlanId(DataSyncPlan plan) =>
        Sha256Hex(CanonicalJson.SerializeToUtf8Bytes(ToJson(plan with { PlanId = "" }, forId: true)))[..16];

    /// <summary>The plan's canonical JSON: the determinism tests compare it byte for byte.</summary>
    public static byte[] CanonicalBytes(DataSyncPlan plan) => CanonicalJson.SerializeToUtf8Bytes(ToJson(plan, false));

    /// <summary>
    /// A hand-built JSON form (never a serializer's, which would turn a lone surrogate in a local name into U+FFFD).
    /// With <paramref name="forId"/>, every InUseCount is left out.
    /// </summary>
    private static JsonObject ToJson(DataSyncPlan plan, bool forId) => new()
    {
        ["planId"] = plan.PlanId,
        ["snapshotContentHash"] = plan.SnapshotContentHash,
        ["kinds"] = Array(plan.Kinds, s => new JsonObject
        {
            ["kind"] = s.Kind,
            ["schemaVersion"] = s.SchemaVersion,
            ["supported"] = s.Supported,
            ["localOnlyCount"] = s.LocalOnlyCount,
            ["items"] = Array(s.Items, i => Item(i, forId)),
        }),
        ["summary"] = new JsonObject
        {
            ["counts"] = Array(plan.Summary.Counts, c => new JsonObject
            {
                ["kind"] = c.Kind, ["type"] = (int)c.Type, ["count"] = c.Count,
            }),
            ["pendingCount"] = plan.Summary.PendingCount,
            ["bulkLinkEligibleCount"] = plan.Summary.BulkLinkEligibleCount,
            ["heldCount"] = plan.Summary.HeldCount,
        },
        ["warnings"] = Array(plan.Warnings, Warning),
    };

    private static JsonObject Item(DataSyncPlanItem item, bool forId)
    {
        var json = new JsonObject
        {
            ["itemId"] = item.ItemId,
            ["kind"] = item.Kind,
            ["type"] = (int)item.Type,
            ["incoming"] = Entity(item.Incoming),
            ["candidates"] = Array(item.Candidates, c => Candidate(c, forId)),
            ["changes"] = Array(item.Changes, c => Change(c, forId)),
            ["changeCounts"] = Counts(item.ChangeCounts),
            ["changesTruncated"] = item.ChangesTruncated,
            ["unchangedChildren"] = item.UnchangedChildren,
            ["localOnlyChildren"] = item.LocalOnlyChildren,
            ["allowedResolutions"] = Array(item.AllowedResolutions, r => JsonValue.Create((int)r)),
            ["requiresConfirmation"] = item.RequiresConfirmation,
            ["bulkLinkEligible"] = item.BulkLinkEligible,
            ["offersSeparateName"] = item.OffersSeparateName,
            ["recordsNewKeys"] = item.RecordsNewKeys,
            ["reviewToken"] = item.ReviewToken,
            ["warnings"] = Array(item.Warnings, Warning),
            ["warningCounts"] = Array(item.WarningCounts, WarningCount),
            ["warningsTruncated"] = item.WarningsTruncated,
        };
        if (item.Reason is { } reason) json["reason"] = (int)reason;
        if (item.HeldReason is { } held) json["heldReason"] = (int)held;
        if (item.Local is { } local) json["local"] = Entity(local);
        if (item.DefaultResolution is { } resolution) json["defaultResolution"] = (int)resolution;
        if (item.DefaultTargetLocalKey is { } target) json["defaultTargetLocalKey"] = target;
        return json;
    }

    private static JsonObject Candidate(DataSyncPlanCandidate candidate, bool forId)
    {
        var json = new JsonObject
        {
            ["localKey"] = candidate.LocalKey,
            ["name"] = candidate.Name,
            ["match"] = (int)candidate.Match,
            ["changes"] = Array(candidate.Changes, c => Change(c, forId)),
            ["changeCounts"] = Counts(candidate.ChangeCounts),
            ["changesTruncated"] = candidate.ChangesTruncated,
            ["warnings"] = Array(candidate.Warnings, Warning),
            ["warningCounts"] = Array(candidate.WarningCounts, WarningCount),
            ["warningsTruncated"] = candidate.WarningsTruncated,
            ["unchangedChildren"] = candidate.UnchangedChildren,
            ["localOnlyChildren"] = candidate.LocalOnlyChildren,
            ["recordsNewKeys"] = candidate.RecordsNewKeys,
            ["reviewToken"] = candidate.ReviewToken,
        };
        if (candidate.Subtype is { } subtype) json["subtype"] = subtype;
        return json;
    }

    private static JsonObject Entity(DataSyncPlanEntity entity)
    {
        var json = new JsonObject
        {
            ["name"] = entity.Name, ["position"] = entity.Position, ["childCount"] = entity.ChildCount,
        };
        if (entity.LocalKey is { } localKey) json["localKey"] = localKey;
        if (entity.Subtype is { } subtype) json["subtype"] = subtype;
        return json;
    }

    private static JsonObject Change(DataSyncFieldChange change, bool forId)
    {
        var json = new JsonObject
        {
            ["changeId"] = change.ChangeId, ["kind"] = (int)change.Kind, ["path"] = change.Path,
        };
        if (change.From is { } from) json["from"] = Display(from);
        if (change.To is { } to) json["to"] = Display(to);
        if (change.DependsOnChangeId is { } dependsOn) json["dependsOnChangeId"] = dependsOn;
        if (!forId && change.InUseCount is { } inUse) json["inUseCount"] = inUse;
        return json;
    }

    private static JsonObject Warning(DataSyncPlanWarning warning)
    {
        var json = new JsonObject { ["code"] = (int)warning.Code };
        if (warning.ChangeId is { } changeId) json["changeId"] = changeId;
        if (warning.Args is { } args)
        {
            var obj = new JsonObject();
            foreach (var (key, value) in args.OrderBy(a => a.Key, StringComparer.Ordinal)) obj[key] = value;
            json["args"] = obj;
        }

        return json;
    }

    private static JsonObject WarningCount(DataSyncWarningCount count) =>
        new() { ["code"] = (int)count.Code, ["count"] = count.Count };

    private static JsonObject Counts(DataSyncChangeCounts counts) => new()
    {
        ["total"] = counts.Total, ["set"] = counts.Set, ["add"] = counts.Add, ["rename"] = counts.Rename,
        ["recolor"] = counts.Recolor,
    };

    private static JsonObject Display(DataSyncDisplayValue value)
    {
        var json = new JsonObject();
        if (value.Text is not null) json["text"] = value.Text;
        if (value.Color is not null) json["color"] = value.Color;
        if (value.Group is not null) json["group"] = value.Group;
        if (value.Path is not null)
            json["path"] = new JsonArray(value.Path.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        if (value.Flag is { } flag) json["flag"] = flag;
        if (value.Number is { } number) json["number"] = number;
        return json;
    }

    private static JsonArray Array<T>(IEnumerable<T> items, Func<T, JsonNode?> map) =>
        new(items.Select(map).ToArray());

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
