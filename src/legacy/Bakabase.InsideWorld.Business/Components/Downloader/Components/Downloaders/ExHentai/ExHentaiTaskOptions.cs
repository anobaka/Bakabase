using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiTaskOptions
    {
        public bool PreferTorrent { get; set; } = true;

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
    }

    /// <summary>Nullable mirror of ExHentaiTaskOptions for parsing: tells an absent value apart from an explicit one. Keep members in sync.</summary>
    internal class ExHentaiTaskOptionsPatch
    {
        public bool? PreferTorrent { get; set; }
        public DateTime? NoTorrentCheckedAt { get; set; }
        public DateTime? TorrentFoundAt { get; set; }
    }
}
