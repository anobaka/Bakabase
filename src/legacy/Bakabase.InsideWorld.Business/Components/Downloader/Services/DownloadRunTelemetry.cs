using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>One event per real downloader run, across scoped status handlers.
/// Weak keys disappear with the downloader; task IDs, paths and keys are never collected.</summary>
internal static class DownloadRunTelemetry
{
    private static readonly ConditionalWeakTable<IDownloader, Run> Runs = new();

    private sealed class Run
    {
        public string AttemptId = Guid.NewGuid().ToString("D");
        public long? Started;
        public bool Completed;
        public TelemetryActor? Actor;
    }

    internal static void Begin(IDownloader downloader, ITelemetryActorAccessor? accessor)
    {
        try
        {
            var run = Runs.GetValue(downloader, _ => new Run());
            lock (run)
            {
                if (run.Started.HasValue && !run.Completed) return;
                run.AttemptId = Guid.NewGuid().ToString("D");
                run.Started = Stopwatch.GetTimestamp();
                run.Completed = false;
                run.Actor = accessor?.CurrentActor;
            }
        }
        catch { /* Telemetry must not prevent a download from starting. */ }
    }

    internal static void Complete(IDownloader downloader, DownloaderStatus status, DownloaderStopBy? stoppedBy,
        IProductTelemetry telemetry)
    {
        var outcome = status switch
        {
            DownloaderStatus.Complete => "success",
            DownloaderStatus.Failed => "failed",
            DownloaderStatus.Stopped when stoppedBy == DownloaderStopBy.ManuallyStop => "cancelled",
            DownloaderStatus.Stopped when stoppedBy == DownloaderStopBy.Defer => "waiting",
            DownloaderStatus.Stopped => "interrupted",
            _ => null
        };
        if (outcome == null) return;
        try
        {
            var run = Runs.GetValue(downloader, _ => new Run());
            lock (run)
            {
                if (run.Completed) return;
                run.Completed = true;
                var properties = new Dictionary<string, object?>
                {
                    ["outcome"] = outcome,
                    ["attempt_id"] = run.AttemptId,
                    ["download_source"] = downloader.ThirdPartyId switch
                    {
                        ThirdPartyId.Bilibili => "bilibili", ThirdPartyId.ExHentai => "exhentai",
                        ThirdPartyId.Pixiv => "pixiv", ThirdPartyId.Fanbox => "fanbox",
                        ThirdPartyId.Fantia => "fantia", ThirdPartyId.Cien => "cien",
                        ThirdPartyId.Patreon => "patreon", _ => "other"
                    }
                };
                if (run.Started.HasValue)
                    properties["duration_ms"] = (long) Stopwatch.GetElapsedTime(run.Started.Value).TotalMilliseconds;
                // This is elapsed run time (including internal waits/retries), never task age or byte-transfer time.
                telemetry.Track("download_completed", properties, run.Actor);
            }
        }
        catch { /* An analytics failure never changes a persisted terminal state. */ }
    }
}
