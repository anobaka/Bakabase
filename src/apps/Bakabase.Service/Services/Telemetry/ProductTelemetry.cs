using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Service.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Service.Services.Telemetry;

/// <summary>Memory-only bounded delivery; never waits for the network from a business call.</summary>
public sealed class ProductTelemetry : BackgroundService, IProductTelemetry
{
    public const string HttpClientName = "product-telemetry";
    private readonly IBOptions<AppOptions> _appOptions;
    private readonly AnalyticsConfiguration.PostHogOptions _options;
    private readonly TelemetryEnvironment _environment;
    private readonly IHttpContextAccessor _httpContext;
    private readonly IHttpClientFactory _httpClients;
    private readonly ILogger<ProductTelemetry> _logger;
    private readonly Channel<TelemetryEvent> _queue;
    private readonly SemaphoreSlim _flush = new(1, 1);
    private readonly object _consentGate = new();
    private CancellationTokenSource _deliveryPermission = new();
    private long _generation;
    private long _dropped;
    private long _failed;
    private int _disposed;

    public ProductTelemetry(IBOptions<AppOptions> appOptions, IOptions<AnalyticsConfiguration> configuration,
        TelemetryEnvironment environment, IHttpContextAccessor httpContext, IHttpClientFactory httpClients,
        ILogger<ProductTelemetry> logger)
    {
        _appOptions = appOptions;
        _options = configuration.Value.PostHog;
        _environment = environment;
        _httpContext = httpContext;
        _httpClients = httpClients;
        _logger = logger;
        _queue = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(Math.Clamp(_options.QueueCapacity, 1, 4096))
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false });
    }

    public bool Enabled => Volatile.Read(ref _disposed) == 0 && _appOptions.Value.EnableAnonymousDataTracking && TelemetryEnvironment.CanSend(_environment.Environment) &&
                           !string.IsNullOrWhiteSpace(_options.ApiKey) && TelemetryHttpContext.Uuid(_environment.DeploymentId) != null;
    public long DroppedCount => Interlocked.Read(ref _dropped);
    public long FailedCount => Interlocked.Read(ref _failed);

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null, TelemetryActor? actor = null)
    {
        try
        {
            var generation = Interlocked.Read(ref _generation);
            if (!Enabled || !TelemetryEventPolicy.Allows(eventName)) return;
            var context = _httpContext.HttpContext;
            // An HTTP caller without client consent is not a background job.
            if (!TelemetryExecutionContext.HasScope && context != null && TelemetryHttpContext.Read(context) == null) return;
            if (actor != null && TelemetryHttpContext.Validate(actor) == null) return;
            actor = TelemetryHttpContext.Validate(actor ?? (TelemetryExecutionContext.HasScope
                ? TelemetryExecutionContext.CurrentActor : TelemetryHttpContext.Read(context)));
            var values = _environment.Properties();
            foreach (var (key, value) in TelemetryEventPolicy.Sanitize(properties)) values[key] = value;
            values["actor_type"] = actor == null ? "system" : "client";
            values["client_surface"] = actor?.Surface ?? "server";
            values["distinct_id"] = actor == null ? $"v2:deployment:{_environment.DeploymentId}" : $"v2:client:{actor.ClientId}";
            // Neither background tasks nor anonymous clients create person profiles or merge identities.
            values["$process_person_profile"] = false;
            values["$geoip_disable"] = true;
            if (actor != null) values["client_install_id"] = actor.ClientId;
            if (actor?.SessionId != null)
            {
                values["$session_id"] = actor.SessionId;
                values["session_id"] = actor.SessionId;
            }
            if (actor?.OperationId != null) values["operation_id"] = actor.OperationId;
            var item = new TelemetryEvent(eventName, Guid.NewGuid(), DateTimeOffset.UtcNow, values,
                generation);
            lock (_consentGate)
            {
                // An opt-out during property capture invalidates this event, even after re-enabling.
                if (!Enabled || generation != Interlocked.Read(ref _generation)) return;
                if (!_queue.Writer.TryWrite(item)) Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            // Analytics must never turn a successful business operation into a failure.
            Interlocked.Increment(ref _dropped);
        }
    }

    public void DiscardPending()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        CancellationTokenSource revoked;
        lock (_consentGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Increment(ref _generation);
            revoked = _deliveryPermission;
            _deliveryPermission = new CancellationTokenSource();
        }
        // CancelAsync marks the token immediately without waiting on arbitrary transport callbacks.
        _ = revoked.CancelAsync().ContinueWith(completed =>
        {
            _ = completed.Exception;
            revoked.Dispose();
        }, TaskScheduler.Default);
        while (_queue.Reader.TryRead(out _)) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_options.FlushIntervalSeconds, 1, 300)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flush.WaitAsync(cancellationToken);
        try
        {
            if (!Enabled) { DiscardPending(); return; }
            var batch = new List<TelemetryEvent>();
            long generation;
            CancellationToken deliveryPermission;
            lock (_consentGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                generation = Interlocked.Read(ref _generation);
                deliveryPermission = _deliveryPermission.Token;
            }
            while (batch.Count < Math.Clamp(_options.BatchSize, 1, 100) && _queue.Reader.TryRead(out var item))
                if (item.Generation == generation) batch.Add(item);
            if (batch.Count == 0 || !Enabled || generation != Interlocked.Read(ref _generation)) return;
            if (!Uri.TryCreate(_options.ApiHost.TrimEnd('/') + "/batch/", UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https")) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deliveryPermission);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await _httpClients.CreateClient(HttpClientName).PostAsJsonAsync(endpoint, new
                {
                    api_key = _options.ApiKey,
                    batch = batch.Select(e => new { @event = e.Name, uuid = e.Id, timestamp = e.Timestamp, properties = e.Properties })
                }, timeout.Token);
                if (!response.IsSuccessStatusCode) Interlocked.Add(ref _failed, batch.Count);
            }
            catch (Exception)
            {
                Interlocked.Add(ref _failed, batch.Count);
                // No URL, payload, credentials or error details go to log/Sentry.
                _logger.LogDebug("Product telemetry batch could not be delivered; discarded {Count} events", batch.Count);
            }
        }
        finally { _flush.Release(); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await FlushAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_consentGate)
        {
            _deliveryPermission.Cancel();
            _deliveryPermission.Dispose();
        }
        base.Dispose();
    }

    internal sealed record TelemetryEvent(string Name, Guid Id, DateTimeOffset Timestamp,
        Dictionary<string, object?> Properties, long Generation);
}
