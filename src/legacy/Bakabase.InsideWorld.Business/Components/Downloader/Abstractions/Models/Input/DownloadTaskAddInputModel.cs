using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input
{
    public class DownloadTaskAddInputModel
    {
        [Required] public ThirdPartyId ThirdPartyId { get; set; }
        [Required] public int Type { get; set; }
        public List<string> Keys { get; set; } = [];
        public List<string>? Names { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? Checkpoint { get; set; }
        public bool AutoRetry { get; set; } = true;
        [Required] public string DownloadPath { get; set; } = string.Empty;
        public bool IsDuplicateAllowed { get; set; }
        public string? Options { get; set; }
    }
}