using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.TextProcessing
{
    /// <summary>
    /// 
    /// </summary>
    public record TextProcessValue : JsonSerializableValue<TextProcessValue>
    {
        public TextProcessOperation Operation { get; init; }
        public string? Value { get; init; }
        public string? Find { get; set; }
        public string? Replace { get; set; }
        public int? Position { get; set; }
        public bool? Reverse { get; set; }
        public bool? RemoveBefore { get; set; }
        public int? Count { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
    }
}