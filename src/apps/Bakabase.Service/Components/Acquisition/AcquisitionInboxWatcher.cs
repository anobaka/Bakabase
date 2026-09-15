using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>
/// Watches the inbox and offers what settles there to whichever acquisition is waiting for it.
/// <para>
/// The whole arrangement rests on one observation: whatever the drive, whatever the client, the
/// file ends up in a folder. Pointing the browser's download directory at the inbox is the entire
/// configuration, and it works for a site nobody has written an integration for.
/// </para>
/// <para>
/// A file is offered once it has stopped changing — same size two ticks running, no download
/// client's temporary extension, openable exclusively. Offering it earlier would take it away
/// mid-download; the two-tick delay is the price of never doing that.
/// </para>
/// </summary>
public class AcquisitionInboxWatcher(
    IServiceScopeFactory scopeFactory,
    IBOptions<AcquisitionOptions> options,
    ILogger<AcquisitionInboxWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(5);

    private sealed class Seen
    {
        public long Length;
        public int StableTicks;
        public bool Offered;
    }

    private readonly ConcurrentDictionary<string, Seen> _seen = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private string? _watching;

    /// <summary>How many quiet ticks a file needs before it is offered.</summary>
    public const int StableTicksRequired = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Rewatch(options.Value.InboxDirectory);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Inbox] The inbox tick failed");
            }

            try
            {
                await Task.Delay(TickEvery, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One pass over the inbox. Public and cancellation-driven so tests can run it directly
    /// instead of waiting on the loop.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var inbox = options.Value.InboxDirectory;

        if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox)) return;

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settled = new List<string>();

        foreach (var file in new DirectoryInfo(inbox).EnumerateFiles())
        {
            present.Add(file.FullName);

            var seen = _seen.GetOrAdd(file.FullName, _ => new Seen {Length = -1});

            if (seen.Offered) continue;

            if (seen.Length == file.Length)
            {
                seen.StableTicks++;
            }
            else
            {
                seen.Length = file.Length;
                seen.StableTicks = 0;
            }

            if (seen.StableTicks >= StableTicksRequired)
            {
                settled.Add(file.FullName);
            }
        }

        // Forget what is gone, so a file downloaded twice is considered afresh.
        foreach (var path in _seen.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _seen.TryRemove(path, out _);
        }

        if (settled.Count == 0) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var inboxService = scope.ServiceProvider.GetRequiredService<AcquisitionInboxService>();

        foreach (var path in settled)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await inboxService.OfferAsync(path, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Inbox] Could not offer {File}", Path.GetFileName(path));
            }
            finally
            {
                // Offered once, whatever came of it. A file nothing claimed stays in the inbox for
                // the user to hand over by hand; re-offering it every five seconds would only
                // re-decide the same thing.
                if (_seen.TryGetValue(path, out var seen)) seen.Offered = true;
            }
        }
    }

    /// <summary>
    /// The watcher only shortens the wait — the tick above would find everything on its own. It
    /// exists so a file that lands right after a tick is not sitting there for the full interval.
    /// </summary>
    private void Rewatch(string? inbox)
    {
        if (string.Equals(_watching, inbox, StringComparison.OrdinalIgnoreCase)) return;

        _watcher?.Dispose();
        _watcher = null;
        _watching = inbox;
        _seen.Clear();

        if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox)) return;

        try
        {
            _watcher = new FileSystemWatcher(inbox)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Renamed += (_, e) => _seen.TryRemove(e.OldFullPath, out Seen _);
            _watcher.Deleted += (_, e) => _seen.TryRemove(e.FullPath, out Seen _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Inbox] Could not watch {Inbox}; falling back to polling", inbox);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
