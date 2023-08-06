using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models
{
    public class ExHentaiImageLimits
    {
        public int Current { get; set; }
        public int Limit { get; set; }
        public int Rest => Limit - Current;
        public int ResetCost { get; set; }
    }
}
