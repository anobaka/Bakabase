using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    IBOptions<NetworkOptions>? network = null, IEnumerable<ICloudDownloadAccountResolver>? accountResolvers = null,
    DownloadSourceResolver? sourceResolver = null)
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

    private string PreparationCheckKey(PreparationSourceCheck check, CloudSource source) =>
        SourceAvailabilityStore.Hash(JsonSerializer.Serialize(new {scope = "acquisition-preparation-v1", check,
            source.Id, source.MethodId, source.AccountId, source.AccountVersion, sourceKey = CacheKey(source)}));

    internal async Task<SourceAvailabilityResult?> GetPreparationCheckAsync(PreparationSourceCheck check,
        CloudSource source, CancellationToken ct)
    {
        source = await BindAccountScopeAsync(source, ct);
        var key = PreparationCheckKey(check, source);
        var result = await store.ObservationAsync(key, ct);
        if (result == null || result.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        // A capability can arrive in a newer application without changing a saved URL.
        if (result.ProbeId == "reviewed-source-method")
            return result.Evidence.TryGetValue("provider", out var provider) && DownloadMethodCatalog.Find(provider) == null ? result : null;
        // An explicit metadata check elsewhere can contradict this snapshot. Do not
        // continue treating an earlier scoped check as current in that case.
        var newer = await store.ObservationAsync(CacheKey(source), ct);
        if (newer?.CheckedAt > result.CheckedAt) return null;
        return result;
    }

    internal async Task<SourceAvailabilityResult> CheckPreparationAsync(PreparationSourceCheck check,
        CloudSource source, bool force, CancellationToken ct)
    {
        if (!force && await GetPreparationCheckAsync(check, source, ct) is {} cached) return cached;
        source = await BindAccountScopeAsync(source, ct);
        var key = PreparationCheckKey(check, source);
        SourceAvailabilityResult? result = null;
        if (source.ProviderId == "unknown" && sourceResolver != null)
        {
            try
            {
                // The existing resolver only contacts reviewed short-link shapes. A
                // recognized terminal provider with no method is capability evidence.
                var resolved = await sourceResolver.ResolveAsync(source, ct);
                if (resolved.ResolutionChain.Count > 1 && resolved.ProviderId != "unknown" &&
                    DownloadMethodCatalog.Select(resolved) == null)
                    result = new() {Status = SourceAvailabilityStatuses.Unsupported, ReasonCode = "SOURCE_UNSUPPORTED",
                        ProbeId = "reviewed-source-method", Evidence = new() {{"provider", resolved.ProviderId}},
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)};
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or HttpRequestException)
            {
                result = new() {Status = SourceAvailabilityStatuses.Unknown, ReasonCode = "SOURCE_VALIDITY_UNKNOWN",
                    ProbeId = "reviewed-source-resolution", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)};
            }
        }
        result ??= await CheckAsync(source, force, ct);
        // This scope cannot affect a changed plan, another content part or credentials.
        // The underlying provider cache stays available to running workflows.
        await store.SaveObservationAsync(key, result, ct);
        return result;
    }

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
