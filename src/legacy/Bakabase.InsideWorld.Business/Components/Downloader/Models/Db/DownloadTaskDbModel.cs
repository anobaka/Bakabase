using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db
{
    public class DownloadTaskDbModel
    {
        public int Id { get; set; }
        [Required] public string Key { get; set; } = string.Empty;
        /// <summary>
        /// Populated during downloading
        /// </summary>
        public string? Name { get; set; }

        public ThirdPartyId ThirdPartyId { get; set; }
        public int Type { get; set; }
        public decimal Progress { get; set; }
        public DateTime DownloadStatusUpdateDt { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? Message { get; set; }
        public string? Checkpoint { get; set; }
        public DownloadTaskStatus Status { get; set; } = DownloadTaskStatus.InProgress;
        public bool AutoRetry { get; set; }
        [Required]
        public string DownloadPath { get; set; } = string.Empty;
    }
}