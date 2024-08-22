using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class ResourcePropertyParsingResult
    {
        public ReservedResourceProperty Property { get; set; }
        public object Value { get; set; }
        public string Key { get; set; }
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }

    public class ResourcePropertyParsingResult<TResult> : ResourcePropertyParsingResult
    {
        public new TResult Result { get; set; }
    }
}