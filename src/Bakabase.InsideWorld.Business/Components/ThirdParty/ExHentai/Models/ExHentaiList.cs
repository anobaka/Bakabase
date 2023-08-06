using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models
{
    public class ExHentaiList
    {
        public int ResultCount { get; set; }
        public string NextListUrl { get; set; }
        public List<ExHentaiResource> Resources { get; set; }
    }
}
