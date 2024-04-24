using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Db
{
    public record Enhancement
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public int Target { get; set; }
        public StandardValueType ValueType { get; set; }
        public string? Value { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}