using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class VolumeRangeAddResult
    {
        public List<Volume> Data { get; set; }
        public int AddedCount { get; set; }
        public int ExistingCount { get; set; }
        public int Count => AddedCount + ExistingCount;
    }
}
