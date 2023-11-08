using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationProcess
    {
        public BulkModificationProperty Property { get; set; }
        public BulkModificationProcessOperation Operation { get; set; }
        public bool RegexEnabled { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }
}