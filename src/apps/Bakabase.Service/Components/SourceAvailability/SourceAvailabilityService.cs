using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.SourceAvailability;

/// <summary>Shared conservative observations. Checking never buys, transfers, downloads or edits a running execution.</summary>
public sealed class SourceAvailabilityService(SourceAvailabilityStore store, IEnumerable<ISourceAvailabilityProbe> probes,
    IBOptions<NetworkOptions>? network = null, IEnumerable<ICloudDownloadAccountResolver>? accountResolvers = null)
{
    private readonly Dictionary<string, ICloudDownloadAccountResolver> _accountResolvers =
        (accountResolvers ?? []).ToDictionary(a => a.AccountProvider, StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly SemaphoreSlim _networkGate = new(4, 4);
    private readonly Dictionary<string, SemaphoreSlim> _providers = new(StringComparer.Ordinal);
    private string CacheKey(CloudSource source)
    {
        var probe = probes.FirstOrDefault(p => p.CanHandle(source));
        return SourceAvailabilityStore.Hash(JsonSerializer.Serialize(new
        {
            schema = 2, probe = probe?.GetType().FullName, probeVersion = probe?.CacheVersion,
            source.ProviderId, source.Url, source.Kind, source.AccessCode,
            // Public metadata has no account context; account-capable adapters must isolate their observations.
            method = probe?.UsesAccountScope == true ? source.MethodId : null,
            account = probe?.UsesAccountScope == true ? source.AccountId : null,
            generation = probe?.UsesAccountScope == true ? source.AccountVersion : null,
            network = network?.Value
        }));
    }
    internal static string DecisionKey(string context, CloudSource source) => SourceAvailabilityStore.Hash(JsonSerializer.Serialize(new
        {context, source.ProviderId, source.Url, source.Kind, source.AccessCode}));
    public async Task<SourceAvailabilityResult?> GetAsync(CloudSource source, CancellationToken ct) =>
        await store.ObservationAsync(CacheKey(await BindAccountScopeAsync(source, ct)), ct);

    private async Task<CloudSource> BindAccountScopeAsync(CloudSource source, CancellationToken ct)
    {
        if (probes.FirstOrDefault(p => p.CanHandle(source))?.UsesAccountScope != true ||
            DownloadMethodCatalog.Select(source)?.AccountProvider is not { } accountProvider ||
            !_accountResolvers.TryGetValue(accountProvider, out var resolver)) return source;
        try
        {
            var account = await resolver.ResolveAccountAsync(source.AccountId, source.AccountVersion, ct);
            return source with {AccountId = account.AccountId, AccountVersion = account.AccountVersion};
        }
        catch (InvalidOperationException)
        {
            // A missing/stale account remains an access requirement. Once configured, the
            // newly resolved generation selects a different cache key without clearing history.
            return source;
        }
    }
    public async Task<bool> IsDisabledAsync(string? contextKey, CloudSource source, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(contextKey) && (await GetDecisionAsync(contextKey, source, ct))?.Disabled == true;
    public Task<SourceAvailabilityDecision?> GetDecisionAsync(string contextKey, CloudSource source, CancellationToken ct) =>
        store.DecisionAsync(DecisionKey(contextKey, source), ct);
    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, bool force, CancellationToken ct)
    {
        source = await BindAccountScopeAsync(source, ct);
        var requestedAt = DateTimeOffset.UtcNow;
        var key = CacheKey(source);
        var gate = _gates[(Convert.ToUInt32(key[..8], 16) % (uint)_gates.Length)];
        await gate.WaitAsync(ct);
        try
        {
            var cached = await store.ObservationAsync(key, ct);
            if (cached?.ExpiresAt > DateTimeOffset.UtcNow && (!force || cached.CheckedAt >= requestedAt)) return cached;
            var probe = probes.FirstOrDefault(p => p.CanHandle(source));
            SourceAvailabilityResult result;
            if (probe == null) result = new() {Status = SourceAvailabilityStatuses.Unsupported, ReasonCode = "PROBE_UNSUPPORTED",
                ProbeId = "none", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)};
            else
            {
                SemaphoreSlim providerGate;
                lock (_providers)
                {
                    if (!_providers.TryGetValue(source.ProviderId, out providerGate!))
                        _providers[source.ProviderId] = providerGate = new(2, 2);
                }
                await providerGate.WaitAsync(ct);
                try
                {
                    await _networkGate.WaitAsync(ct);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(35));
                        try { result = await probe.CheckAsync(source, timeout.Token); }
                        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException && !ct.IsCancellationRequested)
                        { result = new() {Status = SourceAvailabilityStatuses.TemporarilyUnavailable, ReasonCode = "PROBE_FAILED",
                            ProbeId = "guard", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)}; }
                    }
                    finally { _networkGate.Release(); }
                }
                finally { providerGate.Release(); }
            }
            if (result.ExpiresAt <= result.CheckedAt) result = result with {ExpiresAt = result.CheckedAt.AddMinutes(5)};
            await store.SaveObservationAsync(key, result, ct);
            return result;
        }
        finally { gate.Release(); }
    }
    public static bool IsConfirmedUnavailable(SourceAvailabilityResult? result) =>
        result is {Status: SourceAvailabilityStatuses.ConfirmedUnavailable} && result.ExpiresAt > DateTimeOffset.UtcNow;
}
