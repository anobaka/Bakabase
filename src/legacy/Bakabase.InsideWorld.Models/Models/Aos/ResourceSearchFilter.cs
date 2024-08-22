using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record ResourceSearchFilter
    {
        public int PropertyId { get; set; }
        public bool IsCustomProperty { get; set; }
        public SearchOperation Operation { get; set; }
        public string? DbValue { get; set; }
        public string? BizValue { get; set; }
    }
}