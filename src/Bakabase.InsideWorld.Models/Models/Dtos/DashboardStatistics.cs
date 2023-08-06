using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class DashboardStatistics
    {
        public NameAndCount[] TotalResourceCounts { get; set; }
        public NameAndCount[] ResourceAddedCountsToday { get; set; }
        public NameAndCount[] ResourceAddedCountsThisWeek { get; set; }
        public NameAndCount[] Top10TagCounts { get; set; }
        public NameAndCount[] DownloadTaskStatusCounts { get; set; }

        public ThirdPartyRequestStatistics[] ThirdPartyRequestCounts { get; set; }

        public class NameAndCount
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }
    }
}
