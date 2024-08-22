using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures
{
    public class CustomEnhancement : Enhancement
    {
        public CustomDataType DataType { get; set; }
        public bool IsArray { get; set; }
    }
}
