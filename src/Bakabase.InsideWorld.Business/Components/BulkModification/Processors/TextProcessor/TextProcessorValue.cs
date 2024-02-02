using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.SingleValueProcessor;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.TextProcessor
{
    /// <summary>
    /// 
    /// </summary>
    public record TextProcessorValue : JsonProcessValue<string>
    {
        public TextProcessorOperation Operation { get; init; }
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