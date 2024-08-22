using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class SimpleRangeAddResult<TData>
    {
        public Dictionary<string, TData> Data { get; set; }
        public int AddedCount { get; set; }
        public int ExistingCount { get; set; }
        public int Count => AddedCount + ExistingCount;
    }
}
