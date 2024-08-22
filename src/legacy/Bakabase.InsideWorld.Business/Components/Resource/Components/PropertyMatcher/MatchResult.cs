using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher
{
    public class MatchResult
    {
        public MatchResultType Type { get; set; }
        public int? Layer { get; set; }
        public int? Index { get; set; }
        public string[]? Matches { get; set; }

        public static MatchResult OfLayer(int layer, int? index)
        {
            return new MatchResult
            {
                Type = MatchResultType.Layer,
                Layer = layer,
                Index = index
            };
        }

        public static MatchResult OfRegex(string[] matches)
        {
            return new MatchResult
            {
                Type = MatchResultType.Regex,
                Matches = matches
            };
        }
    }
}