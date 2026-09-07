using System;
using System.Text.Json.Serialization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models
{
    /// <summary>
    /// What the app has learned about a task while running it, projected for display.
    ///
    /// Kept separate from <see cref="DownloadTask.Options"/> — which stays the raw, source-shaped
    /// blob the editor round-trips — so the task list does not have to parse per-source JSON and
    /// guess at its meaning. Every member is nullable and means "not applicable / not known yet".
    /// </summary>
    public class DownloadTaskMetadata
    {
        /// <summary>ExHentai only: whether this task downloads a torrent when one exists.</summary>
        public bool? PreferTorrent { get; set; }

        /// <summary>ExHentai only: when a torrent was last found for this gallery.</summary>
        public DateTime? TorrentFoundAt { get; set; }

        /// <summary>ExHentai only: when this gallery was last probed and found to have no torrent.</summary>
        public DateTime? NoTorrentCheckedAt { get; set; }

        /// <summary>
        /// True when nothing is known yet. Server-side only — the whole object is omitted from the
        /// response in that case, so shipping the flag would only add a field the client must ignore.
        /// </summary>
        [JsonIgnore]
        public bool IsEmpty => PreferTorrent == null && TorrentFoundAt == null && NoTorrentCheckedAt == null;
    }
}
