using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.ExHentai
{
    public record ExHentaiRawData
    {
        public decimal? Rating { get; set; }
        public Dictionary<string, List<string>>? Tags { get; set; }
    }
}
