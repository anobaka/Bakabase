using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db
{
    /// <summary>
    /// Permanent record that a given third-party item has been downloaded at least once.
    /// Uniquely identified by (<see cref="ThirdPartyId"/>, <see cref="Key"/>).
    ///
    /// Unlike <see cref="DownloadTaskDbModel"/>, a record here is NOT removed when the user
    /// deletes the originating download task - it lives forever so consumers (e.g. the
    /// baka-monkey userscript) can tell that an item was already downloaded and warn the user.
    /// </summary>
    public class DownloadRecordDbModel
    {
        public int Id { get; set; }
        public ThirdPartyId ThirdPartyId { get; set; }

        /// <summary>
        /// Same value as <see cref="DownloadTaskDbModel.Key"/> - the third-party item identifier
        /// (gallery url, video id, ...). Unique together with <see cref="ThirdPartyId"/>.
        /// </summary>
        [Required]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// The last time a download task with this (ThirdPartyId, Key) completed successfully.
        /// </summary>
        public DateTime DownloadedAt { get; set; } = DateTime.Now;
    }
}
