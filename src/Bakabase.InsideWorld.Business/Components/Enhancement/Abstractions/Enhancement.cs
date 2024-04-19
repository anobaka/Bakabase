using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public record Enhancement
    {
        public int Target { get; set; }
        public StandardValueType ValueType { get; set; }
        public object? Value { get; set; }
        public string? Error { get; set; }
    }
}
