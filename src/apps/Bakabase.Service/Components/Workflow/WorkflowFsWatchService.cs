using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Service.Components.Workflow.Fs;
using Bakabase.Service.Components.Workflow.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow;

/// <summary>
/// The watching half of E6 — the FileMover-intake shape: filesystem watchers over the roots of
/// enabled <c>fs.watch</c> definitions feed a pending set; an entry that stays quiet for a
/// definition's settle period is published to the workflow event bus, pre-filtered per unique
/// TriggerFilterJson (see <see cref="FsWatchTrigger"/> for why the filter travels verbatim in
/// the payload). Definitions are re-read every <see cref="RefreshEvery"/>, so enabling or
/// editing a watch definition takes effect without a restart.
/// <para>The event/settle plumbing (<see cref="NoteFsEvent"/>, <see cref="TickAsync"/>,
/// <see cref="RefreshDefinitionsAsync"/>) is public and clock-parameterized so tests drive it
/// deterministically — the hosted loop is only glue around those three calls.</para>
/// </summary>
public class WorkflowFsWatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowFsWatchService> logger) : BackgroundService
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);

    /// <summary>A pending path no filter wants is dropped after this long.</summary>
    private static readonly TimeSpan PendingMaxAge = TimeSpan.FromHours(1);

    private sealed record FilterGroup(
        string FilterJson,
        FsWatchTrigger.FsWatchFilter Filter,
        List<string> NormalizedRoots);

    private sealed class PendingEntry
    {
        public DateTime LastEventUtc;
        public readonly HashSet<string> PublishedFor = [];
    }

    private readonly ConcurrentDictionary<string, PendingEntry> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);

    private List<FilterGroup> _groups = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRefresh = DateTime.MinValue;
        using var timer = new PeriodicTimer(TickEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (DateTime.UtcNow - lastRefresh >= RefreshEvery)
                {
                    lastRefresh = DateTime.UtcNow;
                    try { await RefreshDefinitionsAsync(stoppingToken); }
                    catch (Exception ex) { logger.LogWarning(ex, "fs.watch definition refresh failed"); }
                }

                try { await TickAsync(DateTime.UtcNow, stoppingToken); }
                catch (Exception ex) { logger.LogWarning(ex, "fs.watch tick failed"); }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            foreach (var w in _watchers.Values) w.Dispose();
            _watchers.Clear();
        }
    }

    /// <summary>Re-read enabled fs.watch definitions and align watchers with their roots.</summary>
    public async Task RefreshDefinitionsAsync(CancellationToken ct = default)
    {
        List<string?> filterJsons;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            filterJsons = await db.Set<WorkflowDefinitionDbModel>()
                .Where(d => d.Enabled && d.TriggerKind == FsWorkflowKinds.TriggerWatch)
                .Select(d => d.TriggerFilterJson)
                .ToListAsync(ct);
        }

        // One group per unique verbatim filter: the publish unit. Definitions sharing the
        // exact filter share one event; the bus then creates one run per matching definition.
        var groups = new List<FilterGroup>();
        foreach (var json in filterJsons.Distinct())
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            FsWatchTrigger.FsWatchFilter? filter;
            try
            {
                filter = JsonSerializer.Deserialize<FsWatchTrigger.FsWatchFilter>(json, WorkflowJson.Options);
            }
            catch (JsonException)
            {
                continue; // an unfinished config just doesn't watch yet
            }

            if (filter is not {Roots.Count: > 0}) continue;
            var roots = filter.Roots
                .Select(NormalizeRoot)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (roots.Count > 0) groups.Add(new FilterGroup(json!, filter, roots));
        }

        _groups = groups;

        var wantedRoots = groups.SelectMany(g => g.NormalizedRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stale in _watchers.Keys.Where(r => !wantedRoots.Contains(r)).ToList())
        {
            _watchers[stale].Dispose();
            _watchers.Remove(stale);
        }

        foreach (var root in wantedRoots.Where(r => !_watchers.ContainsKey(r)))
        {
            try
            {
                // Depth 1 on purpose, matching the scan trigger's default frame: the watch
                // reacts to entries appearing IN the root; descending is expandChildren's job.
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += (sender, e) => NoteFsEvent(e.FullPath);
                watcher.Changed += (sender, e) => NoteFsEvent(e.FullPath);
                watcher.Renamed += (sender, e) =>
                {
                    _pending.TryRemove(e.OldFullPath, out var removed);
                    NoteFsEvent(e.FullPath);
                };
                watcher.Deleted += (sender, e) => _pending.TryRemove(e.FullPath, out var removed);
                watcher.EnableRaisingEvents = true;
                _watchers[root] = watcher;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cannot watch {Root}", root);
            }
        }
    }

    /// <summary>An entry changed on disk — (re)start its settle clock. Public so tests (and the
    /// watcher callbacks) share one entry point.</summary>
    public void NoteFsEvent(string fullPath)
    {
        var entry = _pending.GetOrAdd(fullPath, _ => new PendingEntry());
        lock (entry)
        {
            entry.LastEventUtc = DateTime.UtcNow;
            // Renewed activity re-arms every filter: the entry will fire again once quiet.
            entry.PublishedFor.Clear();
        }
    }

    /// <summary>Publish every pending entry that has settled for each interested filter.</summary>
    public async Task TickAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var groups = _groups;
        foreach (var group in groups)
        {
            List<string>? due = null;
            foreach (var (path, entry) in _pending)
            {
                bool settled, alreadyPublished;
                lock (entry)
                {
                    settled = utcNow - entry.LastEventUtc >= TimeSpan.FromSeconds(Math.Max(1, group.Filter.SettleSeconds));
                    alreadyPublished = entry.PublishedFor.Contains(group.FilterJson);
                }

                if (!settled || alreadyPublished || !WantsPath(group, path)) continue;
                (due ??= []).Add(path);
            }

            if (due is null) continue;
            due.Sort(StringComparer.OrdinalIgnoreCase);

            await using var scope = scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IWorkflowEventBus>();
            await bus.PublishAsync(FsWorkflowKinds.TriggerWatch,
                new FsWatchTrigger.FsWatchPayload {SourceFilterJson = group.FilterJson, Paths = due}, ct);

            foreach (var path in due)
            {
                if (_pending.TryGetValue(path, out var entry))
                {
                    lock (entry) entry.PublishedFor.Add(group.FilterJson);
                }
            }
        }

        // Drop entries that vanished or that nothing will ever want.
        foreach (var (path, entry) in _pending)
        {
            var wanted = groups.Any(g => WantsPath(g, path));
            var tooOld = utcNow - entry.LastEventUtc > PendingMaxAge;
            var gone = !File.Exists(path) && !Directory.Exists(path);
            if ((!wanted && groups.Count > 0) || tooOld || gone)
            {
                _pending.TryRemove(path, out _);
            }
        }
    }

    private static bool WantsPath(FilterGroup group, string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null ||
            !group.NormalizedRoots.Contains(NormalizeRoot(parent), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var isDirectory = Directory.Exists(path);
        if (isDirectory)
        {
            return group.Filter.Target is Bakabase.Abstractions.Models.Domain.Constants.FsScanTarget.Directories
                or Bakabase.Abstractions.Models.Domain.Constants.FsScanTarget.Both;
        }

        if (group.Filter.Target is Bakabase.Abstractions.Models.Domain.Constants.FsScanTarget.Directories)
        {
            return false;
        }

        var extensions = group.Filter.ExtensionFilter
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet();
        return extensions.Count == 0 ||
               extensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
