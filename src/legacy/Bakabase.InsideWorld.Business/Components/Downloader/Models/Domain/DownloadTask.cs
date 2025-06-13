using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Domain
{
    public class DownloadTask
    {
        public int Id { get; set; }
        public string Key { get; set; } = null!;

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
        public DownloadTaskDtoStatus Status { get; set; }
        public string DownloadPath { get; set; } = null!;
        public string? Current { get; set; }
        public int FailureTimes { get; set; }
        public bool AutoRetry { get; set; }
        public DateTime? NextStartDt { get; set; }
        public HashSet<DownloadTaskAction> AvailableActions { get; set; } = new();

        [NotMapped] public string DisplayName => Name ?? Key;

        public bool CanStart => AvailableActions.Contains(DownloadTaskAction.StartManually) ||
                                AvailableActions.Contains(DownloadTaskAction.Restart) ||
                                AvailableActions.Contains(DownloadTaskAction.StartAutomatically);
    }
}