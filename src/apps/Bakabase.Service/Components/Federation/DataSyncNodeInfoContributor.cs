using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Says in this node's <c>/info</c> and handshake what data sync it speaks (§7.4): its contract and the lowest one a
/// reader must speak, the kinds of definition it publishes, and whether devices it approved may read them right now.
/// Readers use it to say "too old" or "not shared" before asking; nothing is decided on it here.
/// </summary>
public sealed class DataSyncNodeInfoContributor(FederationStateStore store, IServiceScopeFactory scopes,
    ILogger<DataSyncNodeInfoContributor> logger) : INodeInfoContributor
{
    private string[]? _kinds;

    public async ValueTask<NodeInfo> ContributeAsync(NodeInfo info, CancellationToken ct) => info with
    {
        DataSyncContractVersion = DataSyncContract.Version,
        DataSyncMinimumPeerContract = DataSyncContract.MinimumPeerVersion,
        DataSyncKinds = _kinds ??= ReadKinds(),
        SharesDefinitions = await store.IsDataSyncSharingEnabledAsync(ct)
    };

    /// <summary>
    /// The kinds this build publishes, as <c>kind@schemaVersion</c>, in apply order. The kinds are registered per
    /// scope (their adapters sit on the database), but what they say about themselves never changes while the process
    /// runs, so they are read once. When they cannot be read, this answer says nothing about kinds (and the next one
    /// tries again) rather than failing <c>/info</c>, which library sharing needs too.
    /// </summary>
    private string[]? ReadKinds()
    {
        try
        {
            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetServices<IDataSyncKind>()
                .Select(kind => kind.Codec.Descriptor)
                .OrderBy(d => Rank(d.Kind))
                .ThenBy(d => d.Kind, StringComparer.Ordinal)
                .Select(d => $"{d.Kind}@{d.SchemaVersion}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "The data sync kinds could not be read for this node's info");
            return null;
        }
    }

    private static int Rank(string kind)
    {
        for (var i = 0; i < DataSyncKindIds.All.Count; i++)
            if (DataSyncKindIds.All[i] == kind) return i;
        return int.MaxValue;
    }
}
