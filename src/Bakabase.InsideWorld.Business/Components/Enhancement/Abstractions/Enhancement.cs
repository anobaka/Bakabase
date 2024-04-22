using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public record Enhancement
    {
        public int Target { get; set; }
        public StandardValueType ValueType { get; set; }
        public object? Value { get; set; }
    }
}