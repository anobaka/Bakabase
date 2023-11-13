using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationProcess
    {
        public ResourceDiffProperty Property { get; set; }
        public BulkModificationDiffOperation Operation { get; set; }
        public bool RegexEnabled { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }
}