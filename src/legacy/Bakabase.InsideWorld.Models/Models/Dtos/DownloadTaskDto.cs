using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class DownloadTaskDto
    {
        public int Id { get; set; }
        public string Key { get; set; }

        /// <summary>
        /// Populated during downloading
        /// </summary>
        public string Name { get; set; }

        public ThirdPartyId ThirdPartyId { get; set; }
        public int Type { get; set; }
        public decimal Progress { get; set; }
        public DateTime DownloadStatusUpdateDt { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string Message { get; set; }
        public string Checkpoint { get; set; }
        public DownloadTaskDtoStatus Status { get; set; }
        public string DownloadPath { get; set; }
        public string Current { get; set; }
        public int FailureTimes { get; set; }
        public DateTime? NextStartDt { get; set; }
        public HashSet<DownloadTaskAction> AvailableActions { get; set; } = new();

        [NotMapped] public string DisplayName => Name ?? Key;

        public bool CanStart => AvailableActions.Contains(DownloadTaskAction.StartManually) ||
                                AvailableActions.Contains(DownloadTaskAction.Restart) ||
                                AvailableActions.Contains(DownloadTaskAction.StartAutomatically);
    }
}