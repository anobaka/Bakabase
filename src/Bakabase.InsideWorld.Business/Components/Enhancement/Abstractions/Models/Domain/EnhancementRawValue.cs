using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain
{
    public record EnhancementRawValue
    {
        public int Target { get; set; }
        public object? Value { get; set; }
        public StandardValueType ValueType { get; set; }
    }
}
