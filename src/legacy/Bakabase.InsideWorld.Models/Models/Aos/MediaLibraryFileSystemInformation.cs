using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record MediaLibraryFileSystemInformation
    {
        public long TotalSize { get; set; }
        public long FreeSpace { get; set; }
        public double UsedPercentage => 100 - FreePercentage;

        public double FreePercentage =>
            TotalSize == 0 ? 0 : double.Parse((FreeSpace / (double) TotalSize * 100).ToString("F2"));

        public double FreeSpaceInGb => double.Parse((FreeSpace / 1024 / 1024 / 1024).ToString("F2"));
        public MediaLibraryFileSystemError Error { get; set; }
    }
}