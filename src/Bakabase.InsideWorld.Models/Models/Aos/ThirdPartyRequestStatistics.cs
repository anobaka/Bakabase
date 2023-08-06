using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class ThirdPartyRequestStatistics
    {
        public ThirdPartyId Id { get; set; }
        /// <summary>
        /// Result type - count
        /// </summary>
        public Dictionary<int, int> Counts { get; set; }
    }
}