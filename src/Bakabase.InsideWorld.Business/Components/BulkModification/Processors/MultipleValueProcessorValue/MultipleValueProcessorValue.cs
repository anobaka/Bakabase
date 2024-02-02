using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.SingleValueProcessor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.MultipleValueProcessorValue
{
    public record MultipleValueProcessorValue<TValue, TChildProcessorValue> : JsonProcessValue<TValue>
    {
        public SimpleValueProcessorOperation Operation { get; init; }
        public MultipleValueProcessorFilterBy? FilterBy { get; set; }
        public string? Find { get; set; }
        public List<TValue>? Value { get; init; }
        public TChildProcessorValue? ChildProcessorValue { get; init; }
    }
}