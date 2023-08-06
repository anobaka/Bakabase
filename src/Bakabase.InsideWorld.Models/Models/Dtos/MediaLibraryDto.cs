using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class MediaLibraryDto : MediaLibrary
    {
        public class SingleMediaLibraryRootPathInformation
        {
            public long TotalSize { get; set; }
            public long FreeSpace { get; set; }
            public double UsedPercentage => 100 - FreePercentage;

            public double FreePercentage =>
                TotalSize == 0 ? 0 : double.Parse((FreeSpace / (double) TotalSize * 100).ToString("F2"));

            public double FreeSpaceInGb => double.Parse((FreeSpace / 1024 / 1024 / 1024).ToString("F2"));
            public MediaLibraryError Error { get; set; }
        }

        public Dictionary<string, SingleMediaLibraryRootPathInformation> RootPathInformation { get; set; } = new();
        public string? CategoryName { get; set; }

        public PathConfigurationDto[] PathConfigurations { get; set; } = { };

        public class PathConfigurationDto : PathConfiguration
        {
            public List<TagDto> FixedTags { get; set; } = new();
        }

        public override string ToString()
        {
            return $"[{Id}]{Name}";
        }
    }
}