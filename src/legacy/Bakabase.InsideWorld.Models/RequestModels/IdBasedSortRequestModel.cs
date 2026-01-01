using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    [Obsolete]
    public class IdBasedSortRequestModel
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
    }
}