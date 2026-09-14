using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiTaskOptions
    {
        public bool PreferTorrent { get; set; } = true;
        /// <summary>Frozen per task. Null preserves the save-only behavior of older tasks.</summary>
        public int? DownloadResultWorkflowId { get; set; }

        /// <summary>
        /// When this gallery was last probed and found to have no torrent.
        /// </summary>
        /// <remarks>
        /// Persisted on the task (unlike the manager's in-memory verdict, which is rebuilt after a
        /// restart) so that re-running a large set of tasks does not re-probe every gallery from
        /// scratch. How long it stays valid is
        /// <see cref="Configurations.Models.Domain.ExHentaiOptions.TorrentCheckValidityHours"/>.
        /// Null means never probed, or probed and a torrent was found.
        /// </remarks>
        public DateTime? NoTorrentCheckedAt { get; set; }

        /// <summary>
        /// When this gallery was last probed and a torrent was found.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="NoTorrentCheckedAt"/>. Only the negative verdict used to be
        /// written down, so "we have never looked" and "we looked and there is a torrent" were
        /// indistinguishable afterwards — which is most of what a user wants to know about a
        /// torrent-priority task. Null means never probed, or probed and no torrent was found.
        /// </remarks>
        public DateTime? TorrentFoundAt { get; set; }

        /// <summary>
        /// When this gallery's torrent was last written to the download folder.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="TorrentFoundAt"/>, which is stamped the moment a torrent is seen
        /// — before a download that may still fail. Only this one means "there is nothing left to
        /// do", which is why the scheduler's pre-check reads it and not the other: taking
        /// <see cref="TorrentFoundAt"/> for done would silently complete tasks whose torrent download
        /// failed.
        ///
        /// It is what lets the pre-check answer "already downloaded?" from the task itself, with no
        /// I/O at all. Tasks downloaded before this field existed have no stamp, so the pre-check
        /// falls back to looking in the folder for them.
        /// </remarks>
        public DateTime? TorrentDownloadedAt { get; set; }
    }

    /// <summary>Nullable mirror of ExHentaiTaskOptions for parsing: tells an absent value apart from an explicit one. Keep members in sync.</summary>
    internal class ExHentaiTaskOptionsPatch
    {
        public bool? PreferTorrent { get; set; }
        private int? _downloadResultWorkflowId;
        public int? DownloadResultWorkflowId
        {
            get => _downloadResultWorkflowId;
            set { _downloadResultWorkflowId = value; DownloadResultWorkflowSpecified = true; }
        }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool DownloadResultWorkflowSpecified { get; private set; }
        public DateTime? NoTorrentCheckedAt { get; set; }
        public DateTime? TorrentFoundAt { get; set; }
        public DateTime? TorrentDownloadedAt { get; set; }
    }
}
