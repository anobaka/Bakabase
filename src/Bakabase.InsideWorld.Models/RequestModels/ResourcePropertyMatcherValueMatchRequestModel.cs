using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record ResourcePropertyMatcherValueMatchRequestModel(string[] Segments)
    {
        public string[] Segments { get; set; } = Segments;

        public PropertyPathSegmentMatcherValue? Value { get; set; }

        /// <summary>
        /// Starts from -1
        /// </summary>
        public int? StartIndex { get; set; }

        /// <summary>
        /// Ends to <see cref="Segments.Length"/>
        /// </summary>
        public int? EndIndex { get; set; }
    }
}