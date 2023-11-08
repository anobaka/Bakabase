using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class ResourceDiff
    {
        public ResourceProperty Property { get; set; }
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
    }
}
