using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    /// <summary>
    /// Decides whether a stored "no torrent" verdict is still worth trusting.
    /// </summary>
    public static class ExHentaiTorrentCheckPolicy
    {
        /// <summary>
        /// True when <paramref name="checkedAt"/> is recent enough, under
        /// <paramref name="validityHours"/>, that the gallery need not be probed again.
        /// </summary>
        /// <param name="checkedAt">When the gallery was last found to have no torrent; null if never.</param>
        /// <param name="validityHours">Validity window in hours. Null or non-positive disables caching.</param>
        /// <param name="now">Current time, injectable for testing.</param>
        public static bool IsNoTorrentVerdictFresh(DateTime? checkedAt, int? validityHours, DateTime now)
        {
            if (!checkedAt.HasValue || !validityHours.HasValue || validityHours.Value <= 0)
            {
                return false;
            }

            var age = now - checkedAt.Value;

            // A verdict stamped in the future means the clock moved backwards; treat it as unusable
            // rather than trusting it until the clock catches up.
            if (age < TimeSpan.Zero)
            {
                return false;
            }

            return age < TimeSpan.FromHours(validityHours.Value);
        }
    }
}
