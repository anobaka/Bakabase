using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// What changed definitions an apply wrote, for the pages that show them to refetch (§8.10.6): the payload of
/// <see cref="DataSyncHubPublisher.AppliedKey"/>. Local keys are bare, as the pages know them (not the apply runner's
/// <c>kind:localKey</c> of <see cref="Apply.DataSyncAppliedEvent"/>).
/// </summary>
/// <param name="Kinds">The kinds with an applied change.</param>
/// <param name="LocalKeys">The local keys of the applied entities, of any of those kinds.</param>
public sealed record DataSyncAppliedHubEvent(IReadOnlyList<string> Kinds, IReadOnlyList<string> LocalKeys);

/// <summary>
/// Pushes data sync to every open window over the UI hub (§8.10.6, F57), after the change is stored:
/// <see cref="StatusKey"/> with the indicator's <c>DataSyncStatusView</c>, and <see cref="AppliedKey"/> with what an
/// apply wrote, so open property and extension group pages refetch.
/// </summary>
public sealed class DataSyncHubPublisher
{
    public const string StatusKey = "DataSyncStatus";
    public const string AppliedKey = "DataSyncApplied";

    private readonly IServiceScopeFactory _scopes;

    public DataSyncHubPublisher(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task PublishStatusAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var hub = scope.ServiceProvider.GetService<IHubContext<WebGuiHub, IWebGuiClient>>();
        if (hub is null) return;
        var status = await new DataSyncViews(scope.ServiceProvider).GetStatusAsync(ct);
        await hub.Clients.All.GetIncrementalData(StatusKey, status);
    }

    /// <summary>What a history entry applied: its applied items' kinds and local keys. Nothing when it applied none.</summary>
    public async Task PublishAppliedAsync(int? applyLogId, CancellationToken ct)
    {
        if (applyLogId is not { } id) return;
        await using var scope = _scopes.CreateAsyncScope();
        var hub = scope.ServiceProvider.GetService<IHubContext<WebGuiHub, IWebGuiClient>>();
        if (hub is null) return;
        var entry = await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetHistoryEntryAsync(id, ct);
        if (entry is null) return;
        var applied = DataSyncHistoryJson.ReadItems(entry.ResultJson)
            .Where(i => i.Outcome == DataSyncItemOutcome.Applied && i.LocalKey is not null)
            .ToList();
        if (applied.Count == 0) return;
        await hub.Clients.All.GetIncrementalData(AppliedKey, new DataSyncAppliedHubEvent(
            applied.Select(i => i.Kind).Distinct(StringComparer.Ordinal).ToList(),
            applied.Select(i => i.LocalKey!).Distinct(StringComparer.Ordinal).ToList()));
    }
}
