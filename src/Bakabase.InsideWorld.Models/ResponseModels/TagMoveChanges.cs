using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public class TagMoveChanges
    {
        public int TagId { get; set; }
        public int? GroupId { get; set; }
        public int? Order { get; set; }
    }
}
