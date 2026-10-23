using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Service.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Service.Services.Telemetry;

/// <summary>HTTP-host lifecycle, independent of an open browser. Heartbeats are never user activity.</summary>
public sealed class RuntimeTelemetry : BackgroundService
{
    private readonly ProductTelemetry _telemetry;
    private readonly AppService _app;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RuntimeTelemetry> _logger;
    private readonly AnalyticsConfiguration.PostHogOptions _options;
    private readonly bool _newInstall;
    private readonly Stopwatch _runtime = Stopwatch.StartNew();
    private State? _state;
    private string? _statePath;
    private DateTimeOffset _nextHeartbeat;

    public RuntimeTelemetry(ProductTelemetry telemetry, AppService app, IHostApplicationLifetime lifetime,
        IServiceScopeFactory scopes, IBOptions<AppOptions> appOptions,
        IOptions<AnalyticsConfiguration> options, ILogger<RuntimeTelemetry> logger)
    {
        _telemetry = telemetry;
        _app = app;
        _lifetime = lifetime;
        _scopes = scopes;
        _logger = logger;
        _options = options.Value.PostHog;
        // Same pre-start ordering as NoticeBaselineInitializer: the host has not recorded this version yet.
        _newInstall = appOptions.Value.IsNotInitialized();
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _telemetry.Track("runtime_started");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(stoppingToken);
            _telemetry.Track("runtime_ready", new Dictionary<string, object?>
            {
                ["startup_duration_ms"] = _runtime.Elapsed.TotalMilliseconds,
                ["readiness_stage"] = "http_listening"
            });
            await ObserveAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(stoppingToken)) await ObserveAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        if (!_telemetry.Enabled) return;
        try
        {
            if (_state == null)
            {
                _statePath = Path.Combine(_app.RequestAppDataDirectory(
                    Bakabase.Abstractions.Components.FileSystem.AppDataStorage.Analytics.RelativePath), "runtime-v2.json");
                _state = ReadState(_statePath);
                if (_state.FirstObservedAt == null)
                {
                    _telemetry.Track("deployment_first_seen", new Dictionary<string, object?>
                    { ["first_seen_kind"] = _newInstall ? "new" : "existing_unknown" });
                    _state.FirstObservedAt = DateTimeOffset.UtcNow;
                }
                // An absent stop can be a power loss, kill, network/consent gap, or old build. It is not a crash verdict.
                _telemetry.Track("runtime_heartbeat", Heartbeat(_state.CleanStop ? "clean" : "unclean_or_unknown"));
                _state.CleanStop = false;
                await SaveStateAsync(cancellationToken);
                _nextHeartbeat = DateTimeOffset.UtcNow.AddMinutes(HeartbeatMinutes);
            }
            if (DateTimeOffset.UtcNow >= _nextHeartbeat)
            {
                _telemetry.Track("runtime_heartbeat", Heartbeat("unknown"));
                _nextHeartbeat = DateTimeOffset.UtcNow.AddMinutes(HeartbeatMinutes);
            }
            if (_state.LastSnapshotAt == null || DateTimeOffset.UtcNow - _state.LastSnapshotAt >=
                TimeSpan.FromHours(Math.Clamp(_options.SnapshotIntervalHours, 1, 168)))
            {
                using var scope = _scopes.CreateScope();
                var snapshot = await scope.ServiceProvider.GetRequiredService<ITelemetrySnapshotService>().GenerateAsync();
                _telemetry.Track("deployment_snapshot", new Dictionary<string, object?>
                {
                    ["media_library_count"] = snapshot.MediaLibraryCount,
                    ["resource_count"] = snapshot.ResourceCount,
                    ["ai_enabled"] = snapshot.AiEnabled,
                    ["has_media_library"] = snapshot.HasMediaLibrary,
                    ["configured_enhancer_count"] = snapshot.EnabledEnhancers?.Count,
                    ["enabled_enhancers"] = snapshot.EnabledEnhancers,
                    ["snapshot_status"] = snapshot.SnapshotStatus
                });
                _state.LastSnapshotAt = DateTimeOffset.UtcNow;
                await SaveStateAsync(cancellationToken);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogDebug("Runtime telemetry observation failed; will retry on the next interval");
        }
    }

    private int HeartbeatMinutes => Math.Clamp(_options.HeartbeatIntervalMinutes, 1, 1440);
    private Dictionary<string, object?> Heartbeat(string previousExit) => new()
    {
        ["uptime_seconds"] = _runtime.Elapsed.TotalSeconds,
        ["heartbeat_interval_minutes"] = HeartbeatMinutes,
        ["queue_dropped_count"] = _telemetry.DroppedCount,
        ["delivery_failed_count"] = _telemetry.FailedCount,
        ["previous_exit"] = previousExit
    };

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!_telemetry.Enabled) return;
        _telemetry.Track("runtime_stopped", new Dictionary<string, object?> { ["uptime_seconds"] = _runtime.Elapsed.TotalSeconds });
        if (_state == null) return;
        _state.CleanStop = true;
        try { await SaveStateAsync(cancellationToken); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    internal static State ReadState(string path)
    {
        try { return JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new State(); }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        if (_statePath == null || !_telemetry.Enabled) return;
        var temporary = _statePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_state), cancellationToken);
        File.Move(temporary, _statePath, true);
    }

    internal sealed class State
    {
        public DateTimeOffset? FirstObservedAt { get; set; }
        public DateTimeOffset? LastSnapshotAt { get; set; }
        public bool CleanStop { get; set; }
    }
}
