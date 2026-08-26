using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    public class ExHentaiAccount
    {
        public string? Name { get; set; }
        public string? Cookie { get; set; }
    }

    [Options(fileKey: "third-party-exhentai")]
    public class ExHentaiOptions: ISimpleDownloaderOptionsHolder, IThirdPartyHttpClientOptions
    {
        public List<ExHentaiAccount>? Accounts { get; set; }

        /// <summary>
        /// Backward compatible: reads/writes first account's cookie.
        /// </summary>
        public string? Cookie
        {
            get => Accounts?.FirstOrDefault()?.Cookie;
            set
            {
                if (Accounts is { Count: > 0 })
                {
                    Accounts[0].Cookie = value;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    Accounts = [new ExHentaiAccount { Cookie = value }];
                }
            }
        }

        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; } = 1000;
        public string? DefaultPath { get; set; }
        public string? NamingConvention { get; set; }

        /// <summary>
        /// Default "prefer torrent" value applied to newly created download tasks.
        /// </summary>
        public bool PreferTorrent { get; set; } = true;

        /// <summary>
        /// When enabled (only meaningful while <see cref="PreferTorrent"/> is on), SingleWork tasks
        /// that turn out to have a torrent are processed first: a task without a torrent yields its
        /// download slot back to the queue after probing, so torrent-bearing tasks are drained before
        /// any image-only task starts downloading. Global runtime switch, not frozen per task.
        /// </summary>
        public bool PrioritizeTasksWithTorrent { get; set; }

        /// <summary>
        /// How long a "this gallery has no torrent" verdict stays valid, in hours.
        /// Null or 0 keeps the previous behaviour of re-probing every time.
        /// </summary>
        /// <remarks>
        /// Re-running a large set of tasks otherwise re-probes every gallery from scratch, and with
        /// the default one-second request interval that walk dominates the run. Within the validity
        /// window a known-torrentless gallery skips the probe entirely. Torrents are only ever added
        /// to a gallery, never removed, so the verdict going stale costs a delayed torrent rather
        /// than a wrong download — which is why this is a plain expiry and not a correctness
        /// mechanism.
        /// </remarks>
        public int? TorrentCheckValidityHours { get; set; }

        public bool SkipExisting { get; set; }
        public int MaxRetries { get; set; }
        public int RequestTimeout { get; set; }
        public bool ShowCover { get; set; }

        /// <summary>
        /// Auto-sync interval in minutes. 0 or null = disabled.
        /// </summary>
        public int? AutoSyncIntervalMinutes { get; set; }
    }
}
