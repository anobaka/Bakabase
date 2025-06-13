using System;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Input
{
    public record DownloadTaskStartRequestModel
    {
        public int[] Ids { get; set; } = [];
        public DownloadTaskActionOnConflict ActionOnConflict { get; set; }
    }
}
