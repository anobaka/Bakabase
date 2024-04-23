using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.Bangumi
{
    public record BangumiEnhancerContext
    {
        public string? Name { get; set; }
        public decimal? Rating { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? Originals { get; set; }
    }
}
