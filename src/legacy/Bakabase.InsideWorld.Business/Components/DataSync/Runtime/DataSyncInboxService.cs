using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// "Needs you" as the facade serves it (§9): the pages and items, what converting would do for a type change, and the
/// boundary of a resolution (§9.2), which checks the batch under the gate and enqueues <c>DataSyncResolve:{batchId}</c>.
/// The task re-derives every item from the stored pending records and applies it (package C's runner); nothing is
/// applied here.
/// </summary>
public sealed class DataSyncInboxService
{
    /// <summary>The largest page the inbox serves.</summary>
    public const int MaxPageSize = 500;

    private readonly IServiceProvider _services;
    private readonly IDataSyncStore _store;

    /// <param name="services">A scope.</param>
    public DataSyncInboxService(IServiceProvider services)
    {
        _services = services;
        _store = services.GetRequiredService<IDataSyncStore>();
    }

    private DataSyncLimits Limits => _services.GetService<DataSyncLimits>() ?? DataSyncLimits.Default;

    /// <summary>A page of items, open ones first (§9); never gated. Every time is UTC.</summary>
    public async Task<DataSyncInboxPage> GetPageAsync(DataSyncInboxQuery query, CancellationToken ct)
    {
        var normalized = query with
        {
            Skip = Math.Max(0, query.Skip),
            Take = Math.Clamp(query.Take, 1, MaxPageSize),
            PeerNodeId = string.IsNullOrWhiteSpace(query.PeerNodeId) ? null : query.PeerNodeId,
            Kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind,
        };
        var page = await _store.QueryInboxAsync(normalized, ct);
        return page with { Items = page.Items.Select(WithUtcTimes).ToList() };
    }

    /// <summary>One item as the inbox shows it; null when it does not exist. Never gated.</summary>
    public async Task<DataSyncInboxItemView?> GetItemAsync(long id, CancellationToken ct)
    {
        var item = await _store.GetItemAsync(id, ct);
        if (item is null) return null;
        var link = item.LinkId is { } linkId ? await _store.GetLinkAsync(linkId, ct) : null;
        return ToView(item, link);
    }

    /// <summary>
    /// What converting to the peer's type would do here, with this device's own values (§9.1 E); null unless the item
    /// is an open type change. Never gated: it only reads.
    /// </summary>
    public async Task<DataSyncTypeChangePreview?> PreviewAsync(long id, CancellationToken ct)
    {
        var item = await _store.GetItemAsync(id, ct);
        if (item is not { Type: DataSyncInboxItemType.TypeChange, ClosedAtUtc: null, LocalKey: { } localKey })
            return null;
        var payload = ReadPayload(item.PayloadJson);
        if (payload.RemoteSubtype is not { } remoteSubtype) return null;
        var kind = _services.GetService<IEnumerable<IDataSyncKind>>()?
            .FirstOrDefault(k => k.Codec.Descriptor.Kind == item.Kind);
        return kind is null ? null : await kind.PreviewSubtypeChangeAsync(localKey, remoteSubtype, ct);
    }

    /// <summary>
    /// The boundary of a resolution (§9.2), run under the gate: every item exists and is open, its token is the one
    /// the person saw, the action is allowed for it, its inputs are valid, and every open conflict of an entity — with
    /// every device — is in the batch. Then <c>DataSyncResolve:{batchId}</c> is enqueued with the explicit list.
    /// </summary>
    public async Task<DataSyncTaskStart> ResolveAsync(DataSyncResolveBatchInput input, CancellationToken ct)
    {
        if (input.Items is not { Count: > 0 }) return Refuse(DataSyncProblemCode.NothingSelected, null);
        var duplicate = input.Items.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return Refuse(DataSyncProblemCode.DecisionsInvalid, $"duplicate:{duplicate.Key}");

        var links = new Dictionary<int, DataSyncLinkDbModel?>();
        var items = new List<DataSyncInboxItemDbModel>();
        foreach (var resolution in input.Items)
        {
            var item = await _store.GetItemAsync(resolution.ItemId, ct);
            if (item is null) return Refuse(DataSyncProblemCode.UnknownItem, Id(resolution.ItemId));
            if (item.ClosedAtUtc is not null) return Refuse(DataSyncProblemCode.InboxItemClosed, Id(item.Id));
            if (!string.Equals(item.Token, resolution.Token, StringComparison.Ordinal))
                return Refuse(DataSyncProblemCode.InboxItemChanged, Id(item.Id));

            DataSyncLinkDbModel? link = null;
            if (item.LinkId is { } linkId && !links.TryGetValue(linkId, out link))
                links[linkId] = link = await _store.GetLinkAsync(linkId, ct);
            var payload = ReadPayload(item.PayloadJson);
            var allowed = DataSyncInboxRules.Allowed(item.Type, item.SubjectPath, payload,
                DataSyncInboxRules.IsEffectivelyTwoWay(link));
            if (!allowed.Contains(resolution.Action))
                return Refuse(DataSyncProblemCode.DecisionsInvalid, $"actionNotAllowed:{item.Id}");
            if (DataSyncInboxRules.ValidateInputs(item, payload, resolution, Limits) is { } invalid)
                return Refuse(DataSyncProblemCode.DecisionsInvalid, $"{invalid}:{item.Id}");
            items.Add(item);
        }

        // A partial resolution would give the entity a revision that dominates the other devices' versions, and the
        // next pull would settle the rest in this device's favour, silently (§9.2).
        var batch = input.Items.Select(i => i.ItemId).ToHashSet();
        var entities = items.Where(i => DataSyncInboxRules.IsConflict(i.Type))
            .Select(i => (i.Kind, i.SyncKey)).ToHashSet();
        if (entities.Count > 0)
        {
            var missing = (await _store.GetOpenItemsAsync(null, ct))
                .Where(o => DataSyncInboxRules.IsConflict(o.Type) && entities.Contains((o.Kind, o.Key.Value)) &&
                            !batch.Contains(o.Id))
                .Select(o => o.Id)
                .OrderBy(id => id)
                .ToList();
            if (missing.Count > 0)
                return Refuse(DataSyncProblemCode.ResolveTogether, string.Join(",", missing));
        }

        var launcher = _services.GetRequiredService<DataSyncTaskLauncher>();
        var batchId = Guid.NewGuid().ToString("N")[..16];
        var thenApply = input.Items.Any(r => r.Action == DataSyncInboxAction.ApplyAll &&
                                             items.First(i => i.Id == r.ItemId).Type ==
                                             DataSyncInboxItemType.LargeChange);
        var attempt = await launcher.EnqueueResolveAsync(batchId, input.Items,
            new DataSyncApplyOptions(input.BackupBeforeDestructive), thenApply);
        return attempt is null
            ? Refuse(DataSyncProblemCode.Busy, "stopping")
            : new DataSyncTaskStart(attempt.TaskId, null);
    }

    // ---- views -------------------------------------------------------------------------------------------------

    /// <summary>
    /// An item as the inbox shows it: allowed actions follow §9.1 for its link as it is now, a closed item allows
    /// nothing, nothing is pre-chosen, and every time is UTC.
    /// </summary>
    public static DataSyncInboxItemView ToView(DataSyncInboxItemDbModel item, DataSyncLinkDbModel? link)
    {
        var payload = ReadPayload(item.PayloadJson);
        var allowed = item.ClosedAtUtc is null
            ? DataSyncInboxRules.Allowed(item.Type, item.SubjectPath, payload,
                DataSyncInboxRules.IsEffectivelyTwoWay(link))
            : [];
        return new DataSyncInboxItemView(item.Id, item.LinkId, item.PeerNodeId, link?.PeerName ?? payload.PeerName,
            item.Kind, item.LocalKey, item.Type, item.Origin, item.SubjectPath, payload, allowed, null, item.Token,
            DataSyncViews.Utc(item.CreatedAtUtc), DataSyncViews.Utc(item.UpdatedAtUtc),
            DataSyncViews.Utc(item.ClosedAtUtc), item.Closure, item.Action, item.ClosedByName);
    }

    private static DataSyncInboxItemView WithUtcTimes(DataSyncInboxItemView item) => item with
    {
        CreatedAt = DataSyncViews.Utc(item.CreatedAt),
        UpdatedAt = DataSyncViews.Utc(item.UpdatedAt),
        ClosedAt = DataSyncViews.Utc(item.ClosedAt),
    };

    /// <summary>The stored payload (display values only); an empty card when it does not parse.</summary>
    public static DataSyncInboxPayload ReadPayload(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonSerializer.Deserialize<DataSyncInboxPayload>(json, DataSyncJson.Options) is { } payload)
                    return payload with { Fields = payload.Fields ?? [] };
            }
            catch (JsonException)
            {
            }
        }

        return new DataSyncInboxPayload(string.Empty, null, null, null, null, [], null, null, null, 0, null, null,
            null, null, null);
    }

    private static string Id(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static DataSyncTaskStart Refuse(DataSyncProblemCode code, string? detail) =>
        new(null, new DataSyncProblem(code, detail));
}
