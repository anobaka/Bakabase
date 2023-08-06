using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class MatcherValue
    {
        public string? FixedText { get; set; }
        public int? Layer { get; set; }
        public string? Regex { get; set; }
        public ResourceProperty Property { get; set; }
        public ResourceMatcherValueType ValueType { get; set; }
        public string? Key { get; set; }

        public bool IsValid => (Property != ResourceProperty.CustomProperty || Key.IsNotEmpty()) &&
                               ValueType switch
                               {
                                   ResourceMatcherValueType.Layer => Layer.HasValue && Layer != 0,
                                   ResourceMatcherValueType.Regex => Regex.IsNotEmpty(),
                                   ResourceMatcherValueType.FixedText => false,
                                   _ => throw new ArgumentOutOfRangeException()
                               };
    }
}